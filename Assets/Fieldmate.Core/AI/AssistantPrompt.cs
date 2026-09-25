using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;

namespace Fieldmate.AI;

/// <summary>
/// The assistant's system prompt and per-turn context (design.md §5.4). The system prompt stays constant per language
/// so it caches; everything that changes (step, telemetry, gaze, manual sections) goes into the context block.
/// </summary>
public static class AssistantPrompt
{
    private static readonly Regex Citation = new(@"\s*\[[a-z0-9_]+(?:\.[a-z0-9_]+)+\]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.Compiled);

    public static string System(string machineName, string language)
    {
        var reply = language == "de" ? "Reply in German." : "Reply in English.";
        return $@"You are Fieldmate, a voice assistant helping a technician maintain the {machineName} in mixed reality.
The user hears your replies as speech and sees them on a panel.

Rules:
- Answer in one to three short spoken sentences. Plain words, no lists, no markdown.
- Ground facts about the machine in the manual sections inside <context>. Cite the section id in square brackets, e.g. [fault.overpressure]. If the context does not cover the question, say the manual does not cover it; never invent values or parts.
- When you use tools, first write one very short sentence saying what you are doing (it is spoken while the tools run), and call all the tools the request needs together in one response.
- Use tools to act, not just talk: highlight_part when the user should find a part; read_telemetry for live values; start_procedure only after the user agrees to begin; go_to_step to show a step again (it never completes a step, only real actions do); show_manual when the user wants to read details; log_note when asked to note something; set_language when asked to switch language; identify_view when the user asks what they are looking at.
- Safety first: mention lockout and stored pressure when they matter. Never say a step is done unless the context says so.
- {reply}";
    }

    /// <summary>Per-turn context block. Any argument may be null when unknown.</summary>
    public static string Context(ProcedureRunner runner, TelemetryModel telemetry, PartInfo gazePart, ManualSlice manual)
    {
        var sb = new StringBuilder();
        sb.Append("Procedure: ").Append(DescribeProcedure(runner)).Append('\n');
        if (telemetry != null)
        {
            sb.Append("Telemetry: ").Append(DescribeTelemetry(telemetry)).Append('\n');
        }

        sb.Append("Looking at: ").Append(gazePart != null ? $"{gazePart.Name} ({gazePart.Id})" : "nothing in particular").Append('\n');
        if (manual != null && manual.Sections.Count > 0)
        {
            sb.Append("Manual sections:\n").Append(manual.ToPromptText());
        }
        else
        {
            sb.Append("Manual sections: none matched this question.\n");
        }

        return sb.ToString().TrimEnd();
    }

    public static string DescribeProcedure(ProcedureRunner runner)
    {
        if (runner == null || runner.State == RunnerState.NotStarted)
        {
            return "none running.";
        }

        var definition = runner.Definition;
        if (runner.State == RunnerState.Completed)
        {
            return $"'{definition.Title}' completed (score {runner.Result?.Score}).";
        }

        var step = runner.CurrentStep;
        return $"'{definition.Title}', step {runner.CurrentStepIndex + 1} of {definition.Steps.Count}: {step.Title}.";
    }

    public static string DescribeTelemetry(TelemetryModel telemetry)
    {
        var parts = new List<string>(TelemetryModel.ChannelCount);
        for (var i = 0; i < TelemetryModel.ChannelCount; i++)
        {
            var channel = (TelemetryChannel)i;
            var status = telemetry.Status(channel);
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0} {2} ({3})", channel.ToString().ToLowerInvariant(),
                telemetry[channel], TelemetryModel.Spec(channel).Unit, status == ChannelStatus.Normal ? "normal" : status.ToString().ToUpperInvariant()));
        }

        return string.Join(", ", parts) + ".";
    }

    /// <summary>Removes [section.id] citations so the voice doesn't read them out; the panel keeps them.</summary>
    public static string ForSpeech(string text) => Spaces.Replace(Citation.Replace(text ?? string.Empty, string.Empty), " ").Trim();
}
