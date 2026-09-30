using System.Globalization;
using Fieldmate.Knowledge;

namespace Fieldmate.Procedures;

/// <summary>One short instruction per step for the step panel, built from the step and the manual's part names.</summary>
public static class StepInstructions
{
    public static string For(StepDefinition step, PartCatalog catalog)
    {
        if (step == null)
        {
            return string.Empty;
        }

        var part = Name(step.PartId, catalog);
        return step.Kind switch
        {
            StepKind.Inspect => $"Look at the {part} for a moment.",
            StepKind.Operate when step.PartId == "main_breaker" =>
                $"Grab the breaker bar with both hands and turn it to {step.TargetState.ToUpperInvariant()}.",
            StepKind.Operate when step.TargetState == "removed" => $"Grab the {part} and pull it off towards you.",
            StepKind.Operate => $"Turn the {part} to {step.TargetState}.",
            StepKind.Tool => $"Take the {Name(step.ToolId, catalog)} from the tray and push it into the {part}.",
            StepKind.Measure => $"Look at the {part}: it should read {step.ExpectedValue.ToString("0.#", CultureInfo.InvariantCulture)} ± " +
                                $"{step.Tolerance.ToString("0.#", CultureInfo.InvariantCulture)} {step.Unit}.",
            StepKind.Confirm => "Press Done when finished.",
            _ => string.Empty,
        };
    }

    private static string Name(string partId, PartCatalog catalog) =>
        partId == null ? "part" : (catalog?.DisplayName(partId) ?? partId).ToLowerInvariant();
}
