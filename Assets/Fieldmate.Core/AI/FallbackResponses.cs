using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fieldmate.Knowledge;
using Fieldmate.Localization;
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
/// user is never left without the next action. Understands and answers in English and German (#24), following
/// <see cref="Lang.Current"/>; the panel banner says the assistant is offline.
/// </summary>
public sealed class FallbackResponses
{
    private readonly MachineManual manual;
    private readonly ProcedureRunner runner;
    private readonly TelemetryModel telemetry;
    private readonly PartCatalog catalog;

    // English | German (#24): STT returns German text while German is on.
    private static readonly Regex Start = new(@"\b(start|begin|let'?s go|go ahead|starten|starte|beginnen|fang an|los geht'?s|leg los)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Where = new(@"\b(where|find|show|which one|point|wo|finde|zeig|zeige|welche[rs]?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Reading = new(@"\b(pressure|temperature|vibration|current|reading|telemetry|gauge|status|druck|temperatur|strom|anzeige|messwert[e]?|manometer)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Why = new(@"\b(why|alarm|fault|wrong|problem|cause|warum|wieso|störung|fehler|ursache)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Safety = new(@"\b(safe|safety|lockout|lock out|danger|dangerous|hurt|sicher|sicherheit|abschließen|gefahr|gefährlich|verletzen)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Next = new(@"\b(next|now|what (do|should) i|how do i|step|do i|done|finished|complete|weiter|jetzt|was (soll|muss) ich|wie (geht|mache)|schritt|fertig|erledigt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        var de = Lang.IsGerman;
        var language = Lang.Current;
        if (Where.IsMatch(text) && TryFindPart(text, out var part))
        {
            var shown = part.In(language);
            return new FallbackReply("where", $"{shown.Name}: {shown.Description} {(de ? "Ich habe es markiert." : "I've highlighted it.")}", part.Id);
        }

        if (Reading.IsMatch(text) && telemetry != null && !Why.IsMatch(text))
        {
            return new FallbackReply("reading", de ? $"Gerade: {DescribeTelemetryDe(telemetry)}" : $"Right now: {AssistantPrompt.DescribeTelemetry(telemetry)}");
        }

        if (Why.IsMatch(text))
        {
            var fault = manual.TryGetSection("fault.overpressure", out var section) ? section : null;
            return fault != null
                ? new FallbackReply("why", Trim(fault.TextIn(language), 240) + $" [{fault.Id}]", "relief_valve", fault.Id)
                : new FallbackReply("why", (de ? "Der Druck ist im Alarm; meist ist das Überdruckventil die Ursache. " : "The pressure is in alarm; the relief valve is the usual cause. ") + StepGuidance(), "relief_valve");
        }

        if (Safety.IsMatch(text))
        {
            return manual.TryGetSection("safety.loto", out var loto)
                ? new FallbackReply("safety", Trim(loto.TextIn(language), 240) + $" [{loto.Id}]", null, loto.Id)
                : new FallbackReply("safety", de ? "Schließ den Hauptschalter ab und bau den Druck ab, bevor du etwas öffnest." : "Lock out the main breaker and bleed the pressure before opening anything.");
        }

        if (Start.IsMatch(text) && runner.State != RunnerState.Running)
        {
            return new FallbackReply("start", de
                ? "Ich bin gerade offline und kann den Ablauf nicht per Sprache starten. Drück Start in deinem Handmenü, dann führt dich die Schrittkarte."
                : "I'm offline right now, so I can't start it by voice. Press Start in your hand menu and the step card will guide you.");
        }

        return new FallbackReply(Next.IsMatch(text) ? "next" : "default", StepGuidance());
    }

    /// <summary>The current step in one or two sentences, from the runner and the manual's step section if there is one.</summary>
    public string StepGuidance()
    {
        if (Lang.IsGerman)
        {
            return StepGuidanceDe();
        }

        if (runner.State == RunnerState.Completed)
        {
            return runner.Result == null
                ? "We're all done. Press Restart in your hand menu to go again."
                : $"We're all done: {Scoring.Explain(runner.Result, runner.Definition.Weights, runner.Definition.TimeLimitSeconds)} Press Restart in your hand menu to go again.";
        }

        if (runner.State != RunnerState.Running)
        {
            return "We haven't started yet. Press Start in your hand menu and I'll guide you through each step.";
        }

        var step = runner.CurrentStep;
        var head = $"Right now, let's {char.ToLowerInvariant(step.Title[0]) + step.Title.Substring(1)}.";
        return manual.TryGetSection($"proc.rv.{step.Id}", out var section) ? $"{head} {Trim(section.Text, 200)}" : head;
    }

    private string StepGuidanceDe()
    {
        if (runner.State == RunnerState.Completed)
        {
            // The score breakdown is model-facing English, so the German line keeps to the verdict.
            return runner.Result == null
                ? "Wir sind fertig. Drück Neustart in deinem Handmenü, um noch einmal anzufangen."
                : $"Wir sind fertig: {runner.Result.Score} von 100 Punkten, {(runner.Result.Passed ? "bestanden" : "nicht bestanden")}. Drück Neustart in deinem Handmenü, um noch einmal anzufangen.";
        }

        if (runner.State != RunnerState.Running)
        {
            return "Wir haben noch nicht angefangen. Drück Start in deinem Handmenü, dann führe ich dich durch jeden Schritt.";
        }

        var step = runner.CurrentStep;
        var head = $"Jetzt: {Lang.T(step.Title)}.";
        return manual.TryGetSection($"proc.rv.{step.Id}", out var section) ? $"{head} {Trim(section.TextIn(Lang.German), 200)}" : head;
    }

    private static readonly string[] ChannelsDe = { "Druck", "Temperatur", "Vibration", "Strom" };

    private static string DescribeTelemetryDe(TelemetryModel telemetry)
    {
        var parts = new List<string>(TelemetryModel.ChannelCount);
        for (var i = 0; i < TelemetryModel.ChannelCount; i++)
        {
            var channel = (TelemetryChannel)i;
            var status = telemetry.Status(channel) switch { ChannelStatus.Alarm => "ALARM", ChannelStatus.Warning => "WARNUNG", _ => "normal" };
            parts.Add($"{ChannelsDe[i]} {German.Number(telemetry[channel])} {TelemetryModel.Spec(channel).Unit} ({status})");
        }

        return string.Join(", ", parts) + ".";
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
            var german = candidate.German?.Name.ToLowerInvariant();
            var hit = lower.Contains(name) ? name.Length
                : german != null && lower.Contains(german) ? german.Length
                : lower.Contains(id) ? id.Length : 0;
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
