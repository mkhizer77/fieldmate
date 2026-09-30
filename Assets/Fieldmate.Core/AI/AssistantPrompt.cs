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

    public static string System(string machineName, string language, bool vision = true)
    {
        var identify = vision ? "; identify_view when the user asks what they are looking at" : string.Empty;
        var reply = language == "de" ? "Reply in German." : "Reply in English.";
        return $@"You are Fieldmate, a voice assistant helping a technician maintain the {machineName} in mixed reality.
The user hears your replies as speech and sees them on a panel.

Rules:
- Keep every reply to one or two short spoken sentences, at most about 30 words, even for broad questions. Give the single most useful point and offer to go on; the user asks for more. Plain words, no lists, no markdown.
- State telemetry exactly as the context gives it, including its status (e.g. pressure 6.8 bar, ALARM means too high).
- Ground facts about the machine in the manual sections inside <context>. Cite the section id in square brackets, e.g. [fault.overpressure]. If the context does not cover the question, say the manual does not cover it; never invent values or parts.
- When you use tools, first write one very short sentence saying what you are doing (it is spoken while the tools run), and call all the tools the request needs together in one response, each tool at most once. After the tool results, always answer with one or two sentences: what happened and what the user should do now for the current step. Never end a turn with only tool calls.
- Use tools to act, not just talk: highlight_part when the user should find a part; read_telemetry for live values; start_procedure as soon as the user agrees to begin ("let's start", "go ahead", "yes", "start it" all count; do not ask again); go_to_step to show a step again (it never completes a step, only real actions do); show_manual when the user wants to read details; log_note when asked to note something; set_language when asked to switch language{identify}.
- If the user can't see or find a highlighted part, call highlight_part again and tell them where it is on the machine from the manual description; never just say it is already highlighted.
- Safety first: mention lockout and stored pressure when they matter.
- The app decides when a step is done, from the user's real actions (looking at a part, turning a control). Never say a step is complete or that you are moving to the next step unless a tool result says so; the context's Procedure line is the only truth. Tell the user what to do for the current step instead.
- When the user tells you a gauge reading, or asks whether the reading is right or the step is done, call report_reading: it reads the machine's gauge and completes the current reading step only if the value fits. Relay its result; if the step is now complete, say what the next step asks.
- {reply}";
    }

    /// <summary>Per-turn context block. Any argument may be null when unknown.</summary>
    public static string Context(ProcedureRunner runner, TelemetryModel telemetry, PartInfo gazePart, ManualSlice manual)
    {
        var sb = new StringBuilder();
        sb.Append("Procedure: ").Append(DescribeProcedure(runner, telemetry)).Append('\n');
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

    public static string DescribeProcedure(ProcedureRunner runner) => DescribeProcedure(runner, null);

    public static string DescribeProcedure(ProcedureRunner runner, TelemetryModel telemetry)
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
        return $"'{definition.Title}', step {runner.CurrentStepIndex + 1} of {definition.Steps.Count}: {step.Title}. {Needs(runner, step, telemetry)}";
    }

    /// <summary>What the current step still needs, so the model can guide instead of guessing.</summary>
    public static string Needs(ProcedureRunner runner, StepDefinition step, TelemetryModel telemetry)
    {
        switch (step.Kind)
        {
            case StepKind.Measure:
                var gauge = telemetry != null
                    ? string.Format(CultureInfo.InvariantCulture, "the gauge now reads {0:0.0} {1}", telemetry[TelemetryChannel.Pressure], step.Unit)
                    : "gauge value unknown";
                return string.Format(CultureInfo.InvariantCulture, "Needs a gauge reading of {0:0.#} ± {1:0.#} {2} while the user looks at the {3}; {4}.",
                    step.ExpectedValue, step.Tolerance, step.Unit, step.PartId, gauge);
            case StepKind.Operate:
                var now = runner.GetPartState(step.PartId) ?? "unknown";
                return $"Needs {step.PartId} = {step.TargetState} (now {now}).";
            case StepKind.Inspect:
                return string.Format(CultureInfo.InvariantCulture, "Needs the user to look at the {0} for {1:0.#} s.", step.PartId, step.DwellSeconds);
            case StepKind.Tool:
                return $"Needs the {step.ToolId} fitted into the {step.PartId}.";
            default:
                return string.Empty;
        }
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
