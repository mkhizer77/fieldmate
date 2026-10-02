using Fieldmate.Procedures;

namespace Fieldmate.AI;

/// <summary>
/// When the mate speaks about a safety rule (#90). Every refused attempt may be spoken, not only a rule's first
/// violation (device test 2026-10-02: the breaker was held five times in silence); but one line per attempt (an attempt
/// can break two rules at once), and the same rule not again within <see cref="RepeatSeconds"/> while the user retries.
/// </summary>
public sealed class SafetyLineGate
{
    public const double RepeatSeconds = 6.0;

    private string lastRule;
    private double lastTime = double.NegativeInfinity;

    /// <summary>True when a line about <paramref name="rule"/> should be spoken for an attempt at <paramref name="time"/>.</summary>
    public bool ShouldSpeak(SafetyRule rule, double time)
    {
        if (rule == null)
        {
            return false;
        }

        if (time == lastTime && lastRule != null)
        {
            return false; // the same attempt already got its line
        }

        if (rule.Id == lastRule && time - lastTime < RepeatSeconds)
        {
            return false;
        }

        lastRule = rule.Id;
        lastTime = time;
        return true;
    }

    public void Reset()
    {
        lastRule = null;
        lastTime = double.NegativeInfinity;
    }
}
