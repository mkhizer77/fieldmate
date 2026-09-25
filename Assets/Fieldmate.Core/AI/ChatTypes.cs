using System;
using System.Collections.Generic;

namespace Fieldmate.AI;

public enum ChatRole
{
    User,
    Assistant,

    /// <summary>The result of a tool call, sent back to the model.</summary>
    Tool,
}

public enum StopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
    Other,
}

/// <summary>A tool invocation requested by the model. Arguments stay raw JSON until the registry validates them.</summary>
public sealed class ToolCall
{
    public ToolCall(string id, string name, string argumentsJson)
    {
        Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("Tool call id is required.", nameof(id)) : id;
        Name = name ?? string.Empty;
        ArgumentsJson = argumentsJson;
    }

    public string Id { get; }
    public string Name { get; }
    public string ArgumentsJson { get; }

    public override string ToString() => $"{Name}({ArgumentsJson})";
}

/// <summary>One entry of the conversation, provider-agnostic.</summary>
public sealed class ChatMessage
{
    private static readonly IReadOnlyList<ToolCall> NoCalls = Array.Empty<ToolCall>();

    private ChatMessage(ChatRole role, string text, IReadOnlyList<ToolCall> toolCalls, string toolCallId, bool isError)
    {
        Role = role;
        Text = text ?? string.Empty;
        ToolCalls = toolCalls ?? NoCalls;
        ToolCallId = toolCallId;
        IsError = isError;
    }

    public ChatRole Role { get; }
    public string Text { get; }

    /// <summary>Tool calls requested in an assistant message.</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; }

    /// <summary>For <see cref="ChatRole.Tool"/> messages: the call this result answers.</summary>
    public string ToolCallId { get; }

    /// <summary>For <see cref="ChatRole.Tool"/> messages: the call failed and <see cref="Text"/> explains why.</summary>
    public bool IsError { get; }

    /// <summary>Rough size used for the conversation budget: text, tool arguments and names.</summary>
    public int Characters
    {
        get
        {
            var count = Text.Length;
            for (var i = 0; i < ToolCalls.Count; i++)
            {
                count += ToolCalls[i].Name.Length + (ToolCalls[i].ArgumentsJson?.Length ?? 0);
            }

            return count;
        }
    }

    public static ChatMessage User(string text) => new(ChatRole.User, text, null, null, false);

    public static ChatMessage Assistant(string text, IReadOnlyList<ToolCall> toolCalls = null) =>
        new(ChatRole.Assistant, text, toolCalls, null, false);

    public static ChatMessage ToolResult(string toolCallId, string content, bool isError) =>
        new(ChatRole.Tool, content,
            null, string.IsNullOrEmpty(toolCallId) ? throw new ArgumentException("Tool call id is required.", nameof(toolCallId)) : toolCallId,
            isError);
}

/// <summary>Everything a chat model needs for one turn (design.md §5.4).</summary>
public sealed class ChatRequest
{
    public ChatRequest(string systemPrompt, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools,
        string context = null, int maxOutputTokens = 400)
    {
        SystemPrompt = systemPrompt ?? string.Empty;
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        Tools = tools ?? Array.Empty<ToolDefinition>();
        Context = context ?? string.Empty;
        MaxOutputTokens = maxOutputTokens > 0 ? maxOutputTokens : throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));
    }

    /// <summary>Role and rules of the assistant.</summary>
    public string SystemPrompt { get; }

    public IReadOnlyList<ChatMessage> Messages { get; }
    public IReadOnlyList<ToolDefinition> Tools { get; }

    /// <summary>Per-request context slice: current step, telemetry, gazed part, manual sections.</summary>
    public string Context { get; }

    /// <summary>Kept small: spoken answers must be short (latency budget, design.md §5.4).</summary>
    public int MaxOutputTokens { get; }
}

public sealed class ChatResponse
{
    public ChatResponse(string text, IReadOnlyList<ToolCall> toolCalls, StopReason stopReason)
    {
        Text = text ?? string.Empty;
        ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
        StopReason = stopReason;
    }

    public string Text { get; }
    public IReadOnlyList<ToolCall> ToolCalls { get; }
    public StopReason StopReason { get; }
}
