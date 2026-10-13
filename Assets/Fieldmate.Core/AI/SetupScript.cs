using System;
using Fieldmate.Localization;
using Fieldmate.Procedures;

namespace Fieldmate.AI;

/// <summary>The stages of the guided setup (#71), in order.</summary>
public enum SetupStage
{
    RoomScan,
    MeetMate,
    PlaceMate,
    PlaceMachine,
    Briefing,
    Done,
}

/// <summary>
/// What the hologram mate says during the guided setup (#71). Pure strings so the wording is tested and the input words
/// (pinch / trigger) come from the caller. Short sentences: they are spoken and shown as captions.
/// </summary>
public static class SetupScript
{
    /// <summary>Caption while the headset looks at the room (nothing is spoken yet).</summary>
    public static string RoomScan => Lang.IsGerman
        ? "Ich schaue mir deinen Raum an… Wenn das Headset dich bittet, ihn zu scannen, folge seinen Schritten."
        : "Looking at your room… If the headset asks you to scan it, follow its steps.";

    /// <summary>The mate's first words. <paramref name="pointAndConfirm"/> e.g. "point your right hand where you want me and pinch".</summary>
    public static string Intro(string pointAndConfirm) => Lang.IsGerman
        ? "Hallo, ich bin Fieldmate, dein Kollege für die Wartung. Ich führe dich durch den heutigen Auftrag und achte auf deine " +
          $"Sicherheit. Stell mich zuerst an einen Platz, an dem ich griffbereit, aber nicht im Weg bin: {pointAndConfirm}."
        : "Hi, I'm Fieldmate, your maintenance mate. I'll walk you through today's job and watch out for your safety. " +
        $"First, put me somewhere handy but out of your way: {pointAndConfirm}.";

    /// <summary>After the mate is placed. <paramref name="placeHint"/> e.g. "point at an open spot on the floor and pinch".</summary>
    public static string PlaceMachine(string placeHint) => Lang.IsGerman
        ? "Super, ich bleibe hier. Jetzt holen wir die Maschine dazu. " +
          $"{Capitalise(placeHint)}, um sie abzustellen, und lass genug Platz, um um sie herumzugehen."
        : "Great, I'll stay right here. Now let's bring in the machine. " +
        $"{Capitalise(placeHint)} to place it, and leave enough room to walk around it.";

    /// <summary>What today is about, from the procedure itself, said like a colleague; ends with the first instruction.</summary>
    public static string Briefing(ProcedureDefinition procedure, string firstInstruction)
    {
        if (procedure == null)
        {
            throw new ArgumentNullException(nameof(procedure));
        }

        var steps = procedure.Steps;
        if (Lang.IsGerman)
        {
            var de = $"Heute steht an: {Lang.T(procedure.Title)}. Das sind {WordsDe(steps.Count)} Schritte. Ich führe dich durch jeden " +
                     "und halte dich auf, wenn etwas nicht sicher ist. Los geht's. ";
            return string.IsNullOrWhiteSpace(firstInstruction) ? de + $"Zuerst: {Lang.T(steps[0].Title)}." : de + firstInstruction.Trim();
        }

        var text = $"Today we're going to {Lower(procedure.Title)}. It's {Words(steps.Count)} steps, and I'll walk you through " +
                   "each one and stop you if anything isn't safe. Let's start. ";
        return string.IsNullOrWhiteSpace(firstInstruction) ? text + $"First, {Lower(steps[0].Title)}." : text + firstInstruction.Trim();
    }

    private static readonly string[] SmallNumbers =
        { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

    private static readonly string[] SmallNumbersDe =
        { "null", "ein", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun", "zehn", "elf", "zwölf" };

    private static string WordsDe(int n) => n >= 0 && n < SmallNumbersDe.Length ? SmallNumbersDe[n] : n.ToString();

    private static string Words(int n) => n >= 0 && n < SmallNumbers.Length ? SmallNumbers[n] : n.ToString();

    /// <summary>How long a caption stays up when there is no voice: about 15 characters a second, 2.5 to 9 seconds.</summary>
    public static float ReadSeconds(string line) =>
        string.IsNullOrEmpty(line) ? 0f : Math.Min(9f, Math.Max(2.5f, line.Length / 15f));

    private static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
