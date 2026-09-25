using System.Collections.Generic;
using System.Text;
using Fieldmate.AI;
using Fieldmate.Json;

namespace Fieldmate.Providers;

/// <summary>
/// Maps the provider-agnostic chat types to the Anthropic Messages format the proxy forwards, and back.
/// The per-request context slice goes into the current turn's user message, not the system prompt, so the system
/// prompt and tool list stay byte-identical across requests and prompt caching keeps working.
/// </summary>
public static class AnthropicWire
{
    public static JsonValue ToRequest(ChatRequest request)
    {
        var members = new List<KeyValuePair<string, JsonValue>>();
        if (request.SystemPrompt.Length > 0)
        {
            members.Add(new("system", JsonValue.From(request.SystemPrompt)));
        }

        members.Add(new("messages", Messages(request.Messages, request.Context)));
        if (request.Tools.Count > 0)
        {
            var tools = new List<JsonValue>(request.Tools.Count);
            foreach (var tool in request.Tools)
            {
                tools.Add(JsonValue.Object(
                    ("name", JsonValue.From(tool.Name)),
                    ("description", JsonValue.From(tool.Description)),
                    ("input_schema", tool.ToInputSchema())));
            }

            members.Add(new("tools", JsonValue.Array(tools)));
        }

        members.Add(new("max_tokens", JsonValue.From(request.MaxOutputTokens)));
        return JsonValue.Object(members);
    }

    public static ChatResponse FromResponse(JsonValue response)
    {
        if (response.Kind != JsonKind.Object || response["content"].Kind != JsonKind.Array)
        {
            throw new ProviderException(ProviderException.InvalidResponse, "The chat response had no content.");
        }

        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        foreach (var block in response["content"].Items)
        {
            switch (block["type"].AsString())
            {
                case "text":
                    text.Append(block["text"].AsString(string.Empty));
                    break;
                case "tool_use":
                    calls.Add(new ToolCall(block["id"].AsString("unknown"), block["name"].AsString(string.Empty),
                        block["input"].Kind == JsonKind.Object ? block["input"].ToJson() : "{}"));
                    break;
            }
        }

        var stop = response["stop_reason"].AsString() switch
        {
            "end_turn" => StopReason.EndTurn,
            "tool_use" => StopReason.ToolUse,
            "max_tokens" => StopReason.MaxTokens,
            _ => StopReason.Other,
        };

        return new ChatResponse(text.ToString(), calls, stop);
    }

    private static JsonValue Messages(IReadOnlyList<ChatMessage> messages, string context)
    {
        var contextIndex = context.Length > 0 ? LastUserTextIndex(messages) : -1;
        var result = new List<JsonValue>();
        List<JsonValue> pendingResults = null;

        void FlushResults()
        {
            if (pendingResults == null)
            {
                return;
            }

            result.Add(Message("user", JsonValue.Array(pendingResults)));
            pendingResults = null;
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            switch (message.Role)
            {
                case ChatRole.Tool:
                    // All results for one assistant turn go back in a single user message.
                    (pendingResults ??= new List<JsonValue>()).Add(JsonValue.Object(
                        ("type", JsonValue.From("tool_result")),
                        ("tool_use_id", JsonValue.From(message.ToolCallId)),
                        ("content", JsonValue.From(message.Text)),
                        ("is_error", JsonValue.From(message.IsError))));
                    break;
                case ChatRole.User:
                    FlushResults();
                    result.Add(i == contextIndex
                        ? Message("user", JsonValue.Array(TextBlock($"<context>\n{context}\n</context>"), TextBlock(message.Text)))
                        : Message("user", JsonValue.From(message.Text)));
                    break;
                default:
                    FlushResults();
                    var blocks = new List<JsonValue>();
                    if (message.Text.Length > 0)
                    {
                        blocks.Add(TextBlock(message.Text));
                    }

                    foreach (var call in message.ToolCalls)
                    {
                        var input = JsonReader.TryParse(call.ArgumentsJson ?? "{}", out var parsed, out _) && parsed.Kind == JsonKind.Object
                            ? parsed
                            : JsonValue.Object();
                        blocks.Add(JsonValue.Object(
                            ("type", JsonValue.From("tool_use")),
                            ("id", JsonValue.From(call.Id)),
                            ("name", JsonValue.From(call.Name)),
                            ("input", input)));
                    }

                    result.Add(Message("assistant", JsonValue.Array(blocks)));
                    break;
            }
        }

        FlushResults();
        return JsonValue.Array(result);
    }

    private static int LastUserTextIndex(IReadOnlyList<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.User)
            {
                return i;
            }
        }

        return -1;
    }

    private static JsonValue Message(string role, JsonValue content) =>
        JsonValue.Object(("role", JsonValue.From(role)), ("content", content));

    private static JsonValue TextBlock(string text) =>
        JsonValue.Object(("type", JsonValue.From("text")), ("text", JsonValue.From(text)));
}
