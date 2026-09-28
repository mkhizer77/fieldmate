using System.Globalization;

namespace Fieldmate.Procedures;

/// <summary>
/// Reading a gauge for a Measure step (design.md §3: "verify zero pressure on the gauge (gaze-dwell read)"). A reading
/// is reported to the procedure only when it is within tolerance; otherwise the user gets a hint. Looking at the gauge
/// too early is not a mistake, so it must not count as an error (#12: "no false failures").
/// </summary>
public static class GaugeCheck
{
    /// <summary>Gaze time on the gauge needed to take a reading.</summary>
    public const float ReadSeconds = 1.2f;

    public static bool TryRead(StepDefinition step, float reading, out string hint)
    {
        if (step == null || step.Kind != StepKind.Measure)
        {
            hint = null;
            return false;
        }

        var delta = reading - step.ExpectedValue;
        if (System.Math.Abs(delta) <= step.Tolerance)
        {
            hint = null;
            return true;
        }

        var value = reading.ToString("0.0", CultureInfo.InvariantCulture);
        var expected = step.ExpectedValue.ToString("0.#", CultureInfo.InvariantCulture);
        hint = delta > 0f
            ? $"Gauge reads {value} {step.Unit}, not {expected} yet. Keep watching or check the isolation."
            : $"Gauge reads {value} {step.Unit}, below {expected}. Is the inlet open and the breaker on?";
        return false;
    }
}
