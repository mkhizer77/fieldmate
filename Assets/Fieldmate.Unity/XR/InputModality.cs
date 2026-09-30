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
}
