using System.Globalization;
using Fieldmate.Knowledge;
using Fieldmate.XR;

namespace Fieldmate.Procedures;

/// <summary>
/// Step wording for the step card (a full sentence) and the highlight label (a few words), built from the step, the
/// manual's part names and the active input: "pinch" with hands, "grip" with controllers.
/// </summary>
public static class StepInstructions
{
    public static string For(StepDefinition step, PartCatalog catalog, Modality modality = Modality.Hands)
    {
        if (step == null)
        {
            return string.Empty;
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
            StepKind.Operate => $"{grab} the {part} and turn it along the arrow to {step.TargetState.ToUpperInvariant()}.",
            StepKind.Tool => $"{grab} the {Name(step.ToolId, catalog)} on the tray and push it into the {part}.",
            StepKind.Measure => $"Look at the {part}: it should read {Number(step.ExpectedValue)} ± {Number(step.Tolerance)} {step.Unit}.",
            StepKind.Confirm => $"{InputWords.Press(modality)} Done when finished.",
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

        var hands = modality == Modality.Controllers ? "both controllers" : "both hands";
        return step.Kind switch
        {
            StepKind.Inspect => "look here",
            StepKind.Operate when step.PartId == "main_breaker" => $"{hands} · turn to {step.TargetState.ToUpperInvariant()}",
            StepKind.Operate when step.TargetState == "removed" => $"{InputWords.Grab(modality)} · pull off",
            StepKind.Operate => $"{InputWords.Grab(modality)} · turn to {step.TargetState.ToUpperInvariant()}",
            StepKind.Tool => "fit the new cartridge here",
            StepKind.Measure => $"read it: {Number(step.ExpectedValue)} ± {Number(step.Tolerance)} {step.Unit}",
            _ => string.Empty,
        };
    }

    private static string Name(string partId, PartCatalog catalog) =>
        partId == null ? "part" : (catalog?.DisplayName(partId) ?? partId).ToLowerInvariant();

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Capitalise(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);
}
