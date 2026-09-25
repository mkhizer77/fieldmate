using System;
using System.Collections.Generic;

namespace Fieldmate.AI;

/// <summary>
/// The rolling conversation sent with each request. Trims whole turns (a user message and everything after it up to
/// the next user message) from the oldest end, so a tool call is never separated from its result and the history
/// always starts with a user message. The latest turn is always kept.
/// </summary>
public sealed class ConversationState
{
    private readonly List<ChatMessage> messages = new();

    public ConversationState(int maxMessages = 24, int maxCharacters = 8000)
    {
        MaxMessages = maxMessages >= 2 ? maxMessages : throw new ArgumentOutOfRangeException(nameof(maxMessages));
        MaxCharacters = maxCharacters > 0 ? maxCharacters : throw new ArgumentOutOfRangeException(nameof(maxCharacters));
    }

    public int MaxMessages { get; }
    public int MaxCharacters { get; }
    public IReadOnlyList<ChatMessage> Messages => messages;

    public int Characters
    {
        get
        {
            var total = 0;
            for (var i = 0; i < messages.Count; i++)
            {
                total += messages[i].Characters;
            }

            return total;
        }
    }

    /// <summary>Tool calls of the last assistant message that have no result yet.</summary>
    public IReadOnlyList<ToolCall> PendingToolCalls
    {
        get
        {
            var pending = new List<ToolCall>();
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role != ChatRole.Assistant)
                {
                    continue;
                }

                foreach (var call in messages[i].ToolCalls)
                {
                    if (!HasResultAfter(i, call.Id))
                    {
                        pending.Add(call);
                    }
                }

                break;
            }

            return pending;
        }
    }

    public void AddUser(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("User messages need text.", nameof(text));
        }

        if (PendingToolCalls.Count > 0)
        {
            throw new InvalidOperationException("Answer the pending tool calls before the next user message.");
        }

        Append(ChatMessage.User(text));
    }

    public void AddAssistant(ChatResponse response)
    {
        if (response == null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        RequireOpenTurn();
        if (PendingToolCalls.Count > 0)
        {
            throw new InvalidOperationException("Answer the pending tool calls before the next assistant message.");
        }

        Append(ChatMessage.Assistant(response.Text, response.ToolCalls));
    }

    public void AddToolResult(ToolResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var pending = PendingToolCalls;
        var matches = false;
        foreach (var call in pending)
        {
            matches |= call.Id == result.ToolCallId;
        }

        if (!matches)
        {
            throw new InvalidOperationException($"No pending tool call with id '{result.ToolCallId}'.");
        }

        Append(result.ToMessage());
    }

    public void Clear() => messages.Clear();

    /// <summary>Drops every message after the first <paramref name="count"/> (undoing a turn that failed midway).</summary>
    public void RollbackTo(int count)
    {
        if (count < 0 || count > messages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        messages.RemoveRange(count, messages.Count - count);
    }

    private void Append(ChatMessage message)
    {
        messages.Add(message);
        Trim();
    }

    private void Trim()
    {
        while (messages.Count > MaxMessages || Characters > MaxCharacters)
        {
            var nextTurn = messages.FindIndex(1, m => m.Role == ChatRole.User);
            if (nextTurn < 0)
            {
                return; // only the latest turn is left; keep it even if it is over budget
            }

            messages.RemoveRange(0, nextTurn);
        }
    }

    private void RequireOpenTurn()
    {
        if (messages.Count == 0)
        {
            throw new InvalidOperationException("The conversation starts with a user message.");
        }
    }

    private bool HasResultAfter(int index, string callId)
    {
        for (var i = index + 1; i < messages.Count; i++)
        {
            if (messages[i].Role == ChatRole.Tool && messages[i].ToolCallId == callId)
            {
                return true;
            }
        }

        return false;
    }
}
