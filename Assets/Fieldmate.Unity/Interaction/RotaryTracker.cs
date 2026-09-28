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

    private readonly Vector3 axis;
    private readonly float min;
    private readonly float max;
    private Vector3 previous;
    private bool hasPrevious;

    public RotaryTracker(Vector3 axis, float minDegrees, float maxDegrees, float startDegrees)
    {
        this.axis = axis.normalized;
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
            Angle = Mathf.Clamp(Angle + Vector3.SignedAngle(previous, current, axis), min, max);
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
