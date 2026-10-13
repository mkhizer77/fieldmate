using System.Globalization;
using Fieldmate.Knowledge;
using Fieldmate.Localization;
using Fieldmate.XR;

namespace Fieldmate.Procedures;

/// <summary>
/// Step wording for the step card (a full sentence) and the highlight label (a few words), built from the step, the
/// manual's part names and the active input: "pinch" with hands, "grip" with controllers. German (#24) is built
/// separately: articles and pronouns follow each part's gender, and "auf"/"zu" are the verbs for opening and closing.
/// </summary>
public static class StepInstructions
{
    public static string For(StepDefinition step, PartCatalog catalog, Modality modality = Modality.Hands)
    {
        if (step == null)
        {
            return string.Empty;
        }

        if (Lang.IsGerman)
        {
            return ForDe(step, catalog, modality);
        }

        var part = Name(step.PartId, catalog);
        var grab = Capitalise(InputWords.Grab(modality));
        var hands = modality == Modality.Controllers ? "both controllers" : "both hands";
        return step.Kind switch
        {
            StepKind.Inspect => $"Look at the {part} for a moment.",
            StepKind.Operate when step.PartId == "main_breaker" =>
                $"{grab} the red breaker bar with {hands} and turn it along the arrow to {step.TargetState.ToUpperInvariant()}.",
            StepKind.Operate when step.TargetState == "removed" => $"{grab} the {part} and pull it off towards you.",
            StepKind.Operate when step.TargetState == "fitted" => $"{grab} the {part} and put it back on its seat until it clicks.",
            StepKind.Operate => $"{grab} the {part} and turn it along the arrow to {step.TargetState.ToUpperInvariant()}.",
            StepKind.Tool => $"{grab} the {Name(step.ToolId, catalog)} on the tray and push it into the {part}.",
            StepKind.Measure => $"Look at the {part}: it should read {Number(step.ExpectedValue)} ± {Number(step.Tolerance)} {step.Unit}.",
            StepKind.Confirm => $"{InputWords.Press(modality)} Done when finished.",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The instruction as the mate says it (device test 2026-10-01: lines sounded like a machine): plain spoken words,
    /// states in lower case, no "±", the gesture named the way a person would.
    /// </summary>
    public static string Spoken(StepDefinition step, PartCatalog catalog, Modality modality = Modality.Hands)
    {
        if (step == null)
        {
            return string.Empty;
        }

        if (Lang.IsGerman)
        {
            return SpokenDe(step, catalog, modality);
        }

        var part = Name(step.PartId, catalog);
        var take = modality == Modality.Controllers ? "Grip" : "Pinch";
        var hands = modality == Modality.Controllers ? "both controllers" : "both hands";
        var state = step.TargetState?.ToLowerInvariant();
        return step.Kind switch
        {
            StepKind.Inspect => $"Have a good look at the {part} first.",
            StepKind.Operate when step.PartId == "main_breaker" => $"{take} the red bar with {hands} and turn it until it clicks to {state}.",
            StepKind.Operate when state == "removed" => $"{take} the {part} and pull it off towards you.",
            StepKind.Operate when state == "fitted" => $"{take} the {part} and put it back on until it clicks into place.",
            StepKind.Operate => $"{take} the {part} and turn it to {state}.",
            StepKind.Tool => $"Pick up the {Name(step.ToolId, catalog)} from the tray and push it into the {part}.",
            StepKind.Measure when step.ExpectedValue == 0f => $"Keep an eye on the {part} until it reads zero.",
            StepKind.Measure => $"Check the {part}: it should settle around {Number(step.ExpectedValue)} {step.Unit}.",
            StepKind.Confirm => "Tell me when you're done.",
            _ => string.Empty,
        };
    }

    /// <summary>A few words for the floating label: what to do with the highlighted part.</summary>
    public static string Short(StepDefinition step, Modality modality = Modality.Hands)
    {
        if (step == null)
        {
            return string.Empty;
        }

        if (Lang.IsGerman)
        {
            return ShortDe(step, modality);
        }

        var hands = modality == Modality.Controllers ? "both controllers" : "both hands";
        return step.Kind switch
        {
            StepKind.Inspect => "look here",
            StepKind.Operate when step.PartId == "main_breaker" => $"{hands} · turn to {step.TargetState.ToUpperInvariant()}",
            StepKind.Operate when step.TargetState == "removed" => $"{InputWords.Grab(modality)} · pull off",
            StepKind.Operate when step.TargetState == "fitted" => $"{InputWords.Grab(modality)} · put back on",
            StepKind.Operate => $"{InputWords.Grab(modality)} · turn to {step.TargetState.ToUpperInvariant()}",
            StepKind.Tool => "fit the new cartridge here",
            StepKind.Measure => $"read it: {Number(step.ExpectedValue)} ± {Number(step.Tolerance)} {step.Unit}",
            _ => string.Empty,
        };
    }

    private static string ForDe(StepDefinition step, PartCatalog catalog, Modality modality)
    {
        var part = Accusative(step.PartId, catalog);
        var it = German.Pronoun(Gender(step.PartId, catalog));
        var hands = modality == Modality.Controllers ? "beiden Controllern" : "beiden Händen";
        var state = Lang.T(step.TargetState ?? string.Empty).ToUpperInvariant();
        return step.Kind switch
        {
            StepKind.Inspect => $"Schau dir {part} einen Moment an.",
            StepKind.Operate when step.PartId == "main_breaker" => $"Greif den roten Schaltergriff mit {hands} und dreh ihn in Pfeilrichtung auf {state}.",
            StepKind.Operate when step.TargetState == "removed" => $"Greif {part} und zieh {it} zu dir hin ab.",
            StepKind.Operate when step.TargetState == "fitted" => $"Greif {part} und setz {it} wieder auf, bis es klickt.",
            StepKind.Operate => $"Greif {part} und dreh {it} in Pfeilrichtung auf {state}.",
            StepKind.Tool => $"Greif {Accusative(step.ToolId, catalog)} auf dem Tablett und drück {German.Pronoun(Gender(step.ToolId, catalog))} in {part}.",
            StepKind.Measure => $"Lies {part} ab: Es sollte {German.Number(step.ExpectedValue)} ± {German.Number(step.Tolerance)} {step.Unit} anzeigen.",
            StepKind.Confirm => "Drück auf Fertig, wenn du so weit bist.",
            _ => string.Empty,
        };
    }

    private static string SpokenDe(StepDefinition step, PartCatalog catalog, Modality modality)
    {
        var part = Accusative(step.PartId, catalog);
        var it = German.Pronoun(Gender(step.PartId, catalog));
        var hands = modality == Modality.Controllers ? "beiden Controllern" : "beiden Händen";
        var state = step.TargetState?.ToLowerInvariant();
        return step.Kind switch
        {
            StepKind.Inspect => $"Schau dir zuerst {part} genau an.",
            StepKind.Operate when step.PartId == "main_breaker" => $"Greif den roten Griff mit {hands} und dreh ihn, bis er auf {Lang.T(state)} einrastet.",
            StepKind.Operate when state == "removed" => $"Greif {part} und zieh {it} zu dir hin ab.",
            StepKind.Operate when state == "fitted" => $"Greif {part} und setz {it} wieder auf, bis es klickt.",
            StepKind.Operate when state == "open" => $"Greif {part} und dreh {it} auf.",
            StepKind.Operate when state == "closed" => $"Greif {part} und dreh {it} zu.",
            StepKind.Operate => $"Greif {part} und dreh {it} auf {Lang.T(state)}.",
            StepKind.Tool => $"Nimm {Accusative(step.ToolId, catalog)} vom Tablett und drück {German.Pronoun(Gender(step.ToolId, catalog))} in {part}.",
            StepKind.Measure when step.ExpectedValue == 0f => $"Behalte {part} im Auge, bis es null anzeigt.",
            StepKind.Measure => $"Prüf {part}: Es sollte sich bei etwa {German.Number(step.ExpectedValue)} {step.Unit} einpendeln.",
            StepKind.Confirm => "Sag mir Bescheid, wenn du fertig bist.",
            _ => string.Empty,
        };
    }

    private static string ShortDe(StepDefinition step, Modality modality)
    {
        var hands = modality == Modality.Controllers ? "beide Controller" : "beide Hände";
        var state = Lang.T(step.TargetState ?? string.Empty).ToUpperInvariant();
        return step.Kind switch
        {
            StepKind.Inspect => "hier hinschauen",
            StepKind.Operate when step.PartId == "main_breaker" => $"{hands} · auf {state} drehen",
            StepKind.Operate when step.TargetState == "removed" => $"{InputWords.Grab(modality)} · abziehen",
            StepKind.Operate when step.TargetState == "fitted" => $"{InputWords.Grab(modality)} · wieder aufsetzen",
            StepKind.Operate => $"{InputWords.Grab(modality)} · auf {state} drehen",
            StepKind.Tool => "neuen Einsatz hier einsetzen",
            StepKind.Measure => $"ablesen: {German.Number(step.ExpectedValue)} ± {German.Number(step.Tolerance)} {step.Unit}",
            _ => string.Empty,
        };
    }

    private static string Accusative(string partId, PartCatalog catalog) =>
        catalog != null && partId != null ? catalog.WithArticle(partId, GermanCase.Accusative) : "das Teil";

    private static string Gender(string partId, PartCatalog catalog) =>
        catalog != null && catalog.TryGet(partId, out var part) ? part.German?.Gender : null;

    private static string Name(string partId, PartCatalog catalog) =>
        partId == null ? "part" : (catalog?.DisplayName(partId) ?? partId).ToLowerInvariant();

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Capitalise(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);
}
