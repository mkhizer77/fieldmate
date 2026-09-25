using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.Json;

namespace Fieldmate.AI;

/// <summary>Validated arguments of a tool call.</summary>
public sealed class ToolArguments
{
    public ToolArguments(JsonValue values) => Values = values ?? JsonValue.Object(Array.Empty<KeyValuePair<string, JsonValue>>());

    public JsonValue Values { get; }

    public bool Has(string name) => Values.Has(name);
    public string GetString(string name, string fallback = null) => Values[name].AsString(fallback);
    public int GetInt(string name, int fallback = 0) => Values[name].TryGetInt(out var value) ? value : fallback;
    public double GetNumber(string name, double fallback = 0d) => Values[name].AsNumber(fallback);
    public bool GetBool(string name, bool fallback = false) => Values[name].AsBoolean(fallback);
}

/// <summary>What a tool execution returns to the model. Errors are phrased so the model can correct itself.</summary>
public sealed class ToolResult
{
    private ToolResult(string toolCallId, string toolName, string content, bool isError)
    {
        ToolCallId = toolCallId;
        ToolName = toolName;
        Content = content ?? string.Empty;
        IsError = isError;
    }

    public string ToolCallId { get; }
    public string ToolName { get; }
    public string Content { get; }
    public bool IsError { get; }

    public static ToolResult Success(ToolCall call, string content) => new(call.Id, call.Name, content, false);
    public static ToolResult Failure(ToolCall call, string message) => new(call.Id, call.Name, message, true);

    public ChatMessage ToMessage() => ChatMessage.ToolResult(ToolCallId, Content, IsError);
}

/// <summary>Performs a tool in the scene (highlight a part, start a procedure...). Implemented by MonoBehaviours.</summary>
public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(ToolCall call, ToolArguments arguments, CancellationToken cancellationToken);
}

/// <summary>
/// The tools the assistant may call (design.md §5.4). Validates every call against its schema before anything runs;
/// unknown tools and malformed arguments become error results the model can read and fix, never exceptions.
/// </summary>
public sealed class ToolRegistry
{
    private readonly List<ToolDefinition> definitions = new();
    private readonly Dictionary<string, IToolExecutor> executors = new(StringComparer.Ordinal);

    public IReadOnlyList<ToolDefinition> Definitions => definitions;

    public void Register(ToolDefinition definition, IToolExecutor executor = null)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (TryGetDefinition(definition.Name, out _))
        {
            throw new ArgumentException($"Tool '{definition.Name}' is already registered.", nameof(definition));
        }

        definitions.Add(definition);
        if (executor != null)
        {
            executors[definition.Name] = executor;
        }
    }

    /// <summary>Attaches (or replaces, or with null removes) the executor of a registered tool.</summary>
    public void SetExecutor(string toolName, IToolExecutor executor)
    {
        if (!TryGetDefinition(toolName, out _))
        {
            throw new ArgumentException($"Tool '{toolName}' is not registered.", nameof(toolName));
        }

        if (executor == null)
        {
            executors.Remove(toolName);
        }
        else
        {
            executors[toolName] = executor;
        }
    }

    public bool HasExecutor(string toolName) => toolName != null && executors.ContainsKey(toolName);

    public bool TryGetDefinition(string toolName, out ToolDefinition definition)
    {
        foreach (var candidate in definitions)
        {
            if (candidate.Name == toolName)
            {
                definition = candidate;
                return true;
            }
        }

        definition = null;
        return false;
    }

    /// <summary>Checks a call against its schema. On failure <paramref name="error"/> tells the model what to fix.</summary>
    public bool TryValidate(ToolCall call, out ToolArguments arguments, out string error)
    {
        arguments = null;
        if (call == null)
        {
            throw new ArgumentNullException(nameof(call));
        }

        if (!TryGetDefinition(call.Name, out var definition))
        {
            error = $"Unknown tool '{call.Name}'. Available tools: {AvailableNames()}.";
            return false;
        }

        var json = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;
        if (!JsonReader.TryParse(json, out var values, out var parseError))
        {
            error = $"Arguments for '{definition.Name}' are not valid JSON ({parseError.Message}). Send one JSON object.";
            return false;
        }

        if (values.Kind != JsonKind.Object)
        {
            error = $"Arguments for '{definition.Name}' must be a JSON object, not {Describe(values.Kind)}.";
            return false;
        }

        foreach (var member in values.Members)
        {
            if (!definition.TryGetParameter(member.Key, out _))
            {
                error = $"Unexpected argument '{member.Key}' for '{definition.Name}'. Allowed: {ParameterNames(definition)}.";
                return false;
            }
        }

        foreach (var parameter in definition.Parameters)
        {
            if (!values.TryGet(parameter.Name, out var value) || value.IsNull)
            {
                if (parameter.Required)
                {
                    error = $"Missing required argument '{parameter.Name}' ({parameter.TypeName}) for '{definition.Name}'.";
                    return false;
                }

                continue;
            }

            if (!TryCheck(definition, parameter, value, out error))
            {
                return false;
            }
        }

        arguments = new ToolArguments(values);
        error = null;
        return true;
    }

    /// <summary>
    /// Validates and runs a call. Never throws for model mistakes or executor failures: those become error results.
    /// Cancellation propagates.
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        if (!TryValidate(call, out var arguments, out var error))
        {
            return ToolResult.Failure(call, error);
        }

        if (!executors.TryGetValue(call.Name, out var executor))
        {
            return ToolResult.Failure(call, $"Tool '{call.Name}' is not available right now.");
        }

        try
        {
            return await executor.ExecuteAsync(call, arguments, cancellationToken) ??
                   ToolResult.Failure(call, $"Tool '{call.Name}' returned no result.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return ToolResult.Failure(call, $"Tool '{call.Name}' failed: {e.Message}");
        }
    }

    private static bool TryCheck(ToolDefinition tool, ToolParameter parameter, JsonValue value, out string error)
    {
        var prefix = $"Argument '{parameter.Name}' of '{tool.Name}'";
        error = null;
        switch (parameter.Type)
        {
            case ToolParameterType.String:
                if (!value.TryGetString(out var text))
                {
                    error = $"{prefix} must be a string, got {Describe(value.Kind)}.";
                    return false;
                }

                if (parameter.Required && text.Trim().Length == 0)
                {
                    error = $"{prefix} must not be empty.";
                    return false;
                }

                if (parameter.MaxLength.HasValue && text.Length > parameter.MaxLength.Value)
                {
                    error = $"{prefix} must be at most {parameter.MaxLength.Value} characters, got {text.Length}.";
                    return false;
                }

                if (parameter.AllowedValues.Count > 0 && !Contains(parameter.AllowedValues, text))
                {
                    error = $"{prefix} must be one of: {string.Join(", ", parameter.AllowedValues)}. Got '{text}'.";
                    return false;
                }

                return true;

            case ToolParameterType.Integer:
            case ToolParameterType.Number:
                if (!value.TryGetNumber(out var number))
                {
                    error = $"{prefix} must be {(parameter.Type == ToolParameterType.Integer ? "an integer" : "a number")}, got {Describe(value.Kind)}.";
                    return false;
                }

                if (parameter.Type == ToolParameterType.Integer && !value.TryGetInt(out _))
                {
                    error = $"{prefix} must be a whole number, got {Format(number)}.";
                    return false;
                }

                if ((parameter.Minimum.HasValue && number < parameter.Minimum.Value) ||
                    (parameter.Maximum.HasValue && number > parameter.Maximum.Value))
                {
                    error = $"{prefix} must be between {Format(parameter.Minimum ?? double.MinValue)} and {Format(parameter.Maximum ?? double.MaxValue)}, got {Format(number)}.";
                    return false;
                }

                return true;

            default:
                if (!value.TryGetBoolean(out _))
                {
                    error = $"{prefix} must be true or false, got {Describe(value.Kind)}.";
                    return false;
                }

                return true;
        }
    }

    private string AvailableNames()
    {
        var names = new List<string>(definitions.Count);
        foreach (var definition in definitions)
        {
            names.Add(definition.Name);
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static string ParameterNames(ToolDefinition definition)
    {
        if (definition.Parameters.Count == 0)
        {
            return "no arguments";
        }

        var names = new List<string>(definition.Parameters.Count);
        foreach (var parameter in definition.Parameters)
        {
            names.Add(parameter.Name);
        }

        return string.Join(", ", names);
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        foreach (var candidate in values)
        {
            if (candidate == value)
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(JsonKind kind) => kind switch
    {
        JsonKind.Null => "null",
        JsonKind.Boolean => "a boolean",
        JsonKind.Number => "a number",
        JsonKind.String => "a string",
        JsonKind.Array => "an array",
        _ => "an object",
    };

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
