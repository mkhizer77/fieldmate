using Fieldmate.XR;

namespace Fieldmate.Procedures;

/// <summary>
/// A one-line "how to" for the first step of each interaction kind in a session: where to put the hand, which gesture,
/// and how to move. Shown on the step card's status row (device test 2026-09-30: turning was not obvious).
/// </summary>
public static class InteractionTips
{
    public const float Seconds = 14f;

    /// <summary>One tip per kind: "turn", "turn2", "pull", "tool", "look"; null for steps that need none.</summary>
    public static string Key(StepDefinition step, int hands)
    {
        if (step == null)
        {
            return null;
        }

        return step.Kind switch
        {
            StepKind.Operate when step.TargetState == "removed" => "pull",
            StepKind.Operate => hands > 1 ? "turn2" : "turn",
            StepKind.Tool => "tool",
            StepKind.Inspect => "look",
            _ => null,
        };
    }

    /// <summary>While one hand holds a two-hand control.</summary>
    public static string SecondHand(Modality modality) => modality == Modality.Controllers
        ? "One controller on the bar. Hold the grip on the other end with the second controller, then turn together."
        : "One hand on the bar. Pinch the other end with your other hand, keep both pinched, and turn together.";

    public static string For(StepDefinition step, int hands, Modality modality)
    {
        var controllers = modality == Modality.Controllers;
        return Key(step, hands) switch
        {
            "look" => "Tip: just look at the highlighted part and hold your gaze for a moment.",
            "turn" => controllers
                ? "Tip: put the controller on the handle, hold the grip, and move it around the pivot. Let go to settle."
                : "Tip: put your hand on the handle, pinch thumb and index, keep pinching and move your hand around the pivot. Let go to settle.",
            "turn2" => controllers
                ? "Tip: both controllers on the bar, hold both grips, and turn them together like a steering wheel."
                : "Tip: both hands on the bar, pinch with each, keep both pinched and turn them together like a steering wheel.",
            "pull" => controllers
                ? "Tip: hold the grip on the cover and pull it straight towards you, then let go."
                : "Tip: pinch the cover, pull it straight towards you, then release the pinch.",
            "tool" => controllers
                ? "Tip: hold the grip on the cartridge, carry it to the seat, let go when it snaps."
                : "Tip: pinch the cartridge on the tray, carry it to the seat, release when it snaps in.",
            _ => string.Empty,
        };
    }
}
