namespace Fieldmate.XR;

public enum Modality
{
    Hands,
    Controllers,
}

/// <summary>
/// The words the UI uses for the active input, so every instruction matches what the user holds: pinches with hands,
/// buttons with controllers. Controller mapping: grip or trigger grabs and presses, trigger places, thumbstick rotates,
/// X talks, Y moves the machine.
/// </summary>
public static class InputWords
{
    /// <summary>"pinch" / "grip": how to take hold of a part.</summary>
    public static string Grab(Modality m) => m == Modality.Controllers ? "grip" : "pinch";

    /// <summary>Imperative for pressing a button on the machine.</summary>
    public static string Press(Modality m) => m == Modality.Controllers ? "Press trigger on" : "Pinch";

    public static string Talk(Modality m) => m == Modality.Controllers ? "Hold X to talk" : "Pinch and hold your left middle finger to talk";

    public static string Place(Modality m) => m == Modality.Controllers
        ? "Point at the floor · trigger to place · thumbstick to rotate"
        : "Point your right hand at the floor · pinch to place";

    public static string Move(Modality m) => m == Modality.Controllers ? "Y or Move machine" : "Move machine";

    /// <summary>How to open the hand menu (#73).</summary>
    public static string Menu(Modality m) => m == Modality.Controllers
        ? "Press the menu button on the left controller for the menu"
        : "Turn your left palm towards you for the menu";

    /// <summary>Spoken: how to put the hologram mate down (#71).</summary>
    public static string PlaceMate(Modality m) => m == Modality.Controllers
        ? "point the right controller where you want me and pull the trigger"
        : "point your right hand where you want me and pinch";

    /// <summary>Spoken: how to put the machine down (#71).</summary>
    public static string PlaceMachineSpoken(Modality m) => m == Modality.Controllers
        ? "point the right controller at an open spot on the floor and pull the trigger"
        : "point your right hand at an open spot on the floor and pinch";
}
