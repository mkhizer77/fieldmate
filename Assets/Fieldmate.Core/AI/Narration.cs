using System.Globalization;
using Fieldmate.Procedures;

namespace Fieldmate.AI;

/// <summary>
/// What the assistant says on its own when the machine's state changes (#61): short, spoken lines built from the
/// runner's events, no model round trip, so a violation is called out the moment it happens.
/// </summary>
public static class Narration
{
    public static string StepDone(int doneNumber, int total, StepDefinition next, string nextInstruction)
    {
        if (next == null)
        {
            return $"Step {doneNumber} done.";
        }

        var instruction = string.IsNullOrWhiteSpace(nextInstruction) ? string.Empty : " " + nextInstruction.Trim();
        return $"Step {doneNumber} done. Next, step {doneNumber + 1} of {total}: {next.Title}.{instruction}";
    }

    public static string Violation(SafetyRule rule) => $"Stop. {rule.Description}";

    /// <summary>An action out of order or a wrong reading: what happened, then what is expected now.</summary>
    public static string Mistake(ProcedureError error, int currentNumber, StepDefinition current, string instruction)
    {
        var what = (error.Message ?? "That's not the current step.").Trim();
        var now = current != null ? $" First, step {currentNumber}: {current.Title}." : string.Empty;
        var how = string.IsNullOrWhiteSpace(instruction) ? string.Empty : " " + instruction.Trim();
        return $"{what}{now}{how}";
    }

    public static string Completed(ProcedureResult result)
    {
        var c = CultureInfo.InvariantCulture;
        var verdict = result.Passed ? "passed" : "not passed";
        return string.Format(c, "Procedure complete, {0}. Score {1} out of 100, {2} error{3}, {4} safety violation{5}.",
            verdict, result.Score, result.Errors.Count, result.Errors.Count == 1 ? "" : "s",
            result.Violations.Count, result.Violations.Count == 1 ? "" : "s");
    }
}
