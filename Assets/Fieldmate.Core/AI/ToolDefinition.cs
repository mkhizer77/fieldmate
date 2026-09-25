using System;
using System.Collections.Generic;
using Fieldmate.Json;

namespace Fieldmate.AI;

public enum ToolParameterType
{
    String,
    Integer,
    Number,
    Boolean,
}

/// <summary>One argument of a tool, with the constraints the registry enforces.</summary>
public sealed class ToolParameter
{
    public ToolParameter(string name, ToolParameterType type, string description, bool required = true,
        IReadOnlyList<string> allowedValues = null, double? minimum = null, double? maximum = null, int? maxLength = null)
    {
        Name = ToolDefinition.IsValidName(name) ? name : throw new ArgumentException($"Invalid parameter name '{name}'.", nameof(name));
        Type = type;
        Description = description ?? string.Empty;
        Required = required;
        AllowedValues = allowedValues ?? Array.Empty<string>();
        Minimum = minimum;
        Maximum = maximum;
        MaxLength = maxLength;
        if (AllowedValues.Count > 0 && type != ToolParameterType.String)
        {
            throw new ArgumentException("Allowed values are only supported for strings.", nameof(allowedValues));
        }
    }

    public string Name { get; }
    public ToolParameterType Type { get; }
    public string Description { get; }
    public bool Required { get; }

    /// <summary>Enum values for string parameters; empty means any string.</summary>
    public IReadOnlyList<string> AllowedValues { get; }

    public double? Minimum { get; }
    public double? Maximum { get; }
    public int? MaxLength { get; }

    internal string TypeName => Type switch
    {
        ToolParameterType.String => "string",
        ToolParameterType.Integer => "integer",
        ToolParameterType.Number => "number",
        _ => "boolean",
    };

    internal JsonValue ToSchema()
    {
        var members = new List<KeyValuePair<string, JsonValue>>
        {
            new("type", JsonValue.From(TypeName)),
            new("description", JsonValue.From(Description)),
        };

        if (AllowedValues.Count > 0)
        {
            var values = new List<JsonValue>();
            foreach (var value in AllowedValues)
            {
                values.Add(JsonValue.From(value));
            }

            members.Add(new("enum", JsonValue.Array(values)));
        }

        if (Minimum.HasValue)
        {
            members.Add(new("minimum", JsonValue.From(Minimum.Value)));
        }

        if (Maximum.HasValue)
        {
            members.Add(new("maximum", JsonValue.From(Maximum.Value)));
        }

        if (MaxLength.HasValue)
        {
            members.Add(new("maxLength", JsonValue.From(MaxLength.Value)));
        }

        return JsonValue.Object(members);
    }
}

/// <summary>A tool the model may call: name, description and a JSON Schema for its arguments.</summary>
public sealed class ToolDefinition
{
    public ToolDefinition(string name, string description, params ToolParameter[] parameters)
    {
        Name = IsValidName(name) ? name : throw new ArgumentException($"Invalid tool name '{name}': use snake_case.", nameof(name));
        Description = string.IsNullOrWhiteSpace(description)
            ? throw new ArgumentException("Tools need a description; the model chooses tools by it.", nameof(description))
            : description;
        Parameters = parameters ?? Array.Empty<ToolParameter>();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in Parameters)
        {
            if (parameter == null || !names.Add(parameter.Name))
            {
                throw new ArgumentException($"Tool '{name}' has a missing or duplicate parameter.", nameof(parameters));
            }
        }
    }

    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<ToolParameter> Parameters { get; }

    public bool TryGetParameter(string name, out ToolParameter parameter)
    {
        foreach (var candidate in Parameters)
        {
            if (candidate.Name == name)
            {
                parameter = candidate;
                return true;
            }
        }

        parameter = null;
        return false;
    }

    /// <summary>JSON Schema of the arguments object (the "input_schema" / "parameters" field providers expect).</summary>
    public JsonValue ToInputSchema()
    {
        var properties = new List<KeyValuePair<string, JsonValue>>();
        var required = new List<JsonValue>();
        foreach (var parameter in Parameters)
        {
            properties.Add(new(parameter.Name, parameter.ToSchema()));
            if (parameter.Required)
            {
                required.Add(JsonValue.From(parameter.Name));
            }
        }

        return JsonValue.Object(
            ("type", JsonValue.From("object")),
            ("properties", JsonValue.Object(properties)),
            ("required", JsonValue.Array(required)),
            ("additionalProperties", JsonValue.False));
    }

    /// <summary>snake_case: a lower-case letter, then lower-case letters, digits or underscores; at most 64 characters.</summary>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64 || name[0] < 'a' || name[0] > 'z')
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
