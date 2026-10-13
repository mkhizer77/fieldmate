using Fieldmate.Procedures;

namespace Fieldmate.AI;

/// <summary>
/// What the assistant says on its own when the machine's state changes (#61): short spoken lines built from the
/// runner's events, no model round trip, so a violation is called out the moment it happens. Worded like a colleague
/// beside you, not a status readout (device test 2026-10-01): no "step 2 of 8" (the step card shows progress), varied
/// acknowledgements, and the next task as a sentence. Variation is chosen by step number, so lines are deterministic.
/// </summary>
public static class Narration
{
    private static readonly string[] Acknowledgements = { "Nice work.", "Good, that's done.", "Perfect.", "Great, that's it.", "Well done." };
    private static readonly string[] Transitions = { "Now let's", "Next, let's", "Okay, now we", "Next up, we" };

    public static string StepDone(int doneNumber, int total, StepDefinition next, string nextInstruction)
    {
        var ack = Acknowledgements[(doneNumber - 1) % Acknowledgements.Length];
        if (next == null)
        {
            return $"{ack} That was the last one.";
        }

        var nearlyDone = doneNumber + 1 == total ? " Last step:" : string.Empty;
        var how = string.IsNullOrWhiteSpace(nextInstruction) ? string.Empty : " " + nextInstruction.Trim();
        return $"{ack}{nearlyDone} {Transitions[(doneNumber - 1) % Transitions.Length]} {Lower(next.Title)}.{how}";
    }

    /// <summary>A safety stop: calm, immediate, and what to do instead.</summary>
    public static string Violation(SafetyRule rule) => $"Hold on. {rule.Description}";

    /// <summary>The interlock held the part again (#90): the reason once more, shorter on the way in.</summary>
    public static string StillHeld(SafetyRule rule) => $"Not yet. {rule.Description}";

    /// <summary>
    /// A finished step was undone (#90: the breaker went back off during the last check): what changed, then how to put it
    /// back. <paramref name="partName"/> is the manual's name, <paramref name="instruction"/> the step's spoken instruction.
    /// </summary>
    public static string Undone(string partName, string targetState, string instruction) =>
        $"Careful, the {partName} isn't {targetState} any more. {(instruction ?? string.Empty).Trim()}".TrimEnd();

    /// <summary>
    /// Something done out of order or a wrong reading: no blame, then what to do now. An action that belongs to a later
    /// step is "a bit later"; anything else gets the runner's message.
    /// </summary>
    public static string Mistake(ProcedureError error, int currentNumber, StepDefinition current, string instruction)
    {
        var outOfOrder = current != null && error.StepId != current.Id;
        var what = outOfOrder ? "Careful, that one comes a bit later." : $"Hmm, not quite. {(error.Message ?? string.Empty).Trim()}";
        var now = current != null ? $" First, let's {Lower(current.Title)}." : string.Empty;
        var how = string.IsNullOrWhiteSpace(instruction) ? string.Empty : " " + instruction.Trim();
        return $"{what}{now}{how}";
    }

    public static string Completed(ProcedureResult result)
    {
        var errors = result.Errors.Count;
        var violations = result.Violations.Count;
        if (result.Passed)
        {
            var clean = errors == 0 && violations == 0
                ? "not a single slip"
                : $"{Count(errors, "small slip")}, no safety issues";
            return $"All done, and you passed: {result.Score} out of 100, {clean}. The pump's back in service.";
        }

        return $"That's the job finished, but it didn't pass this time: {result.Score} out of 100, " +
               $"{Count(violations, "safety issue")}. Let's go over what happened.";
    }

    private static string Count(int n, string thing) => n == 1 ? $"one {thing}" : $"{(n == 0 ? "no" : n.ToString())} {thing}s";

    private static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
}
