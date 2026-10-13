using UnityEngine;

namespace Fieldmate.Interaction;

/// <summary>
/// Turns a grabbing hand's motion around an axis into a clamped angle, for valves, levers and the breaker. Works in the
/// pivot's parent space: the hand's position is projected onto the plane normal to the axis and the change in its
/// direction is accumulated, so multi-turn handwheels keep counting past 360°. Positions too close to the axis are
/// ignored (the direction is unstable there). No allocations.
/// </summary>
public sealed class RotaryTracker
{
    /// <summary>Hands closer to the axis than this don't turn anything.</summary>
    public const float MinRadius = 0.015f;

    /// <summary>
    /// More hand rotation than this between two updates is a tracking jump, not a turn: the tracker re-anchors and the
    /// handle stays (device test 2026-10-02, #90: the breaker swung from on to locked in 0.4 s as two ray grips crossed).
    /// </summary>
    public const float MaxStepDegrees = 45f;

    private readonly Vector3 axis;
    private readonly float min;
    private readonly float max;
    private readonly float gain;
    private Vector3 previous;
    private bool hasPrevious;

    /// <param name="turnGain">Handle degrees per degree of hand travel; above 1 makes long turns easier.</param>
    public RotaryTracker(Vector3 axis, float minDegrees, float maxDegrees, float startDegrees, float turnGain = 1f)
    {
        this.axis = axis.normalized;
        gain = Mathf.Max(0.1f, turnGain);
        min = Mathf.Min(minDegrees, maxDegrees);
        max = Mathf.Max(minDegrees, maxDegrees);
        Angle = Mathf.Clamp(startDegrees, min, max);
    }

    public float Angle { get; private set; }
    public float Min => min;
    public float Max => max;

    /// <summary>0 at <see cref="Min"/>, 1 at <see cref="Max"/>.</summary>
    public float Normalized => max > min ? (Angle - min) / (max - min) : 0f;

    /// <summary>Starts a grab at <paramref name="handFromCenter"/> (hand position minus pivot centre).</summary>
    public void Begin(Vector3 handFromCenter) => hasPrevious = TryProject(handFromCenter, out previous);

    /// <summary>Moves the angle by the hand's rotation since the last call; returns the new angle.</summary>
    public float Update(Vector3 handFromCenter)
    {
        if (!TryProject(handFromCenter, out var current))
        {
            return Angle;
        }

        if (hasPrevious)
        {
            var step = Vector3.SignedAngle(previous, current, axis);
            if (Mathf.Abs(step) <= MaxStepDegrees)
            {
                Angle = Mathf.Clamp(Angle + gain * step, min, max);
            }
        }

        previous = current;
        hasPrevious = true;
        return Angle;
    }

    public void End() => hasPrevious = false;

    /// <summary>Sets the angle directly (snapping on release, restoring state).</summary>
    public void Set(float degrees) => Angle = Mathf.Clamp(degrees, min, max);

    private bool TryProject(Vector3 v, out Vector3 projected)
    {
        projected = Vector3.ProjectOnPlane(v, axis);
        return projected.sqrMagnitude >= MinRadius * MinRadius;
    }
}
