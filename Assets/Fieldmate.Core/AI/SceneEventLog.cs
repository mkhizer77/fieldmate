using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fieldmate.AI;

/// <summary>The last few things that happened on the machine, for the model's context (#61): "12 s ago: …".</summary>
public sealed class SceneEventLog
{
    private readonly int capacity;
    private readonly List<(double time, string text)> events = new();

    public SceneEventLog(int capacity = 6) => this.capacity = capacity < 1 ? 1 : capacity;

    public int Count => events.Count;

    public void Add(double time, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        events.Add((time, text.Trim()));
        while (events.Count > capacity)
        {
            events.RemoveAt(0);
        }
    }

    public void Clear() => events.Clear();

    /// <summary>One line, newest last; "none yet" when empty.</summary>
    public string ToPromptText(double now)
    {
        if (events.Count == 0)
        {
            return "none yet.";
        }

        var sb = new StringBuilder();
        foreach (var (time, text) in events)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }

            var ago = now - time;
            sb.Append(ago < 1 ? "just now" : string.Format(CultureInfo.InvariantCulture, "{0:0} s ago", ago)).Append(": ").Append(text);
        }

        return sb.Append('.').ToString();
    }
}
