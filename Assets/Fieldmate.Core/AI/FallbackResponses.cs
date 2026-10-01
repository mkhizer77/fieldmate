using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;

namespace Fieldmate.AI;

/// <summary>What the scripted assistant answered: text for the panel and speech, and what to show in the scene.</summary>
public sealed class FallbackReply
{
    public FallbackReply(string intent, string text, string highlightPartId = null, string sectionId = null)
    {
        Intent = intent;
        Text = text;
        HighlightPartId = highlightPartId;
        SectionId = sectionId;
    }

    public string Intent { get; }
    public string Text { get; }
    public string HighlightPartId { get; }
    public string SectionId { get; }
}

/// <summary>
/// Scripted answers when the language model is unreachable (#16, design.md §5.4 FallbackResponses): a small
/// regex-intent table over the manual, the twin's telemetry and the procedure runner, keyed by the current step so the
/// user is never left without the next action. English only; the panel banner says the assistant is offline.
/// </summary>
public sealed class FallbackResponses
{
    private readonly MachineManual manual;
    private readonly ProcedureRunner runner;
    private readonly TelemetryModel telemetry;
    private readonly PartCatalog catalog;

    private static readonly Regex Start = new(@"\b(start|begin|let'?s go|go ahead)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Where = new(@"\b(where|find|show|which one|point)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Reading = new(@"\b(pressure|temperature|vibration|current|reading|telemetry|gauge|status)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Why = new(@"\b(why|alarm|fault|wrong|problem|cause)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Safety = new(@"\b(safe|safety|lockout|lock out|danger|dangerous|hurt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Next = new(@"\b(next|now|what (do|should) i|how do i|step|do i|done|finished|complete)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public FallbackResponses(MachineManual manual, ProcedureRunner runner, TelemetryModel telemetry)
    {
        this.manual = manual ?? throw new ArgumentNullException(nameof(manual));
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.telemetry = telemetry;
        catalog = PartCatalog.FromManual(manual);
    }

    public FallbackReply Answer(string userText)
    {
        var text = userText ?? string.Empty;
        if (Where.IsMatch(text) && TryFindPart(text, out var part))
        {
            return new FallbackReply("where", $"{part.Name}: {part.Description} I've highlighted it.", part.Id);
        }

        if (Reading.IsMatch(text) && telemetry != null && !Why.IsMatch(text))
        {
            return new FallbackReply("reading", $"Right now: {AssistantPrompt.DescribeTelemetry(telemetry)}");
        }

        if (Why.IsMatch(text))
        {
            var fault = manual.TryGetSection("fault.overpressure", out var section) ? section : null;
            return fault != null
                ? new FallbackReply("why", Trim(fault.Text, 240) + $" [{fault.Id}]", "relief_valve", fault.Id)
                : new FallbackReply("why", "The pressure is in alarm; the relief valve is the usual cause. " + StepGuidance(), "relief_valve");
        }

        if (Safety.IsMatch(text))
        {
            return manual.TryGetSection("safety.loto", out var loto)
                ? new FallbackReply("safety", Trim(loto.Text, 240) + $" [{loto.Id}]", null, loto.Id)
                : new FallbackReply("safety", "Lock out the main breaker and bleed the pressure before opening anything.");
        }

        if (Start.IsMatch(text) && runner.State != RunnerState.Running)
        {
            return new FallbackReply("start", "I'm offline right now, so I can't start it by voice. Press Start in your hand menu and the step card will guide you.");
        }

        return new FallbackReply(Next.IsMatch(text) ? "next" : "default", StepGuidance());
    }

    /// <summary>The current step in one or two sentences, from the runner and the manual's step section if there is one.</summary>
    public string StepGuidance()
    {
        if (runner.State == RunnerState.Completed)
        {
            return $"We're all done: you scored {runner.Result?.Score} out of 100. Press Restart in your hand menu to go again.";
        }

        if (runner.State != RunnerState.Running)
        {
            return "We haven't started yet. Press Start in your hand menu and I'll guide you through each step.";
        }

        var step = runner.CurrentStep;
        var head = $"Right now, let's {char.ToLowerInvariant(step.Title[0]) + step.Title.Substring(1)}.";
        return manual.TryGetSection($"proc.rv.{step.Id}", out var section) ? $"{head} {Trim(section.Text, 200)}" : head;
    }

    private bool TryFindPart(string text, out PartInfo part)
    {
        var lower = text.ToLowerInvariant();
        PartInfo best = null;
        var bestLength = 0;
        foreach (var candidate in catalog.All)
        {
            var name = candidate.Name.ToLowerInvariant();
            var id = candidate.Id.Replace('_', ' ');
            var hit = lower.Contains(name) ? name.Length : lower.Contains(id) ? id.Length : 0;
            if (hit > bestLength)
            {
                best = candidate;
                bestLength = hit;
            }
        }

        part = best;
        return best != null;
    }

    private static string Trim(string text, int max)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length <= max)
        {
            return text;
        }

        var cut = text.LastIndexOf('.', max);
        return cut > max / 2 ? text.Substring(0, cut + 1) : text.Substring(0, max) + "…";
    }
}
