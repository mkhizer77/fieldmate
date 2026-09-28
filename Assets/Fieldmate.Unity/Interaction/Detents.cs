using System;
using UnityEngine;

namespace Fieldmate.Interaction;

/// <summary>
/// Named positions along a rotary range (e.g. 0° "open", 90° "closed") plus unnamed haptic ticks every
/// <see cref="TickDegrees"/>. Reports when the angle enters a named detent (a state change for the procedure) and when it
/// crosses a tick (a haptic click). Also gives the snap target on release. No allocations after construction.
/// </summary>
public sealed class Detents
{
    /// <summary>How close the angle must be to a named detent to be "in" it.</summary>
    public const float Tolerance = 6f;

    /// <summary>On release, the angle snaps to a named detent this close.</summary>
    public const float SnapDistance = 20f;

    private readonly float[] angles;
    private readonly string[] states;
    private int lastTick;

    public Detents(float[] angles, string[] states, float tickDegrees, float startDegrees)
    {
        if (angles == null || states == null || angles.Length != states.Length)
        {
            throw new ArgumentException("Every detent angle needs a state name.");
        }

        this.angles = angles;
        this.states = states;
        TickDegrees = tickDegrees;
        lastTick = TickIndex(startDegrees);
        State = StateAt(startDegrees);
    }

    public float TickDegrees { get; }

    /// <summary>The last named detent reached (it stays until another one is reached).</summary>
    public string State { get; private set; }

    /// <summary>Feeds a new angle. <paramref name="reached"/> is set when a different named detent is entered.</summary>
    public bool Update(float degrees, out string reached, out bool ticked)
    {
        var tick = TickIndex(degrees);
        ticked = tick != lastTick;
        lastTick = tick;

        reached = null;
        var state = StateAt(degrees);
        if (state != null && state != State)
        {
            State = state;
            reached = state;
        }

        return reached != null;
    }

    /// <summary>The angle to rest at after release: the nearest named detent if close, otherwise where it is.</summary>
    public float SnapTarget(float degrees)
    {
        var best = degrees;
        var bestDistance = SnapDistance;
        for (var i = 0; i < angles.Length; i++)
        {
            var distance = Mathf.Abs(angles[i] - degrees);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = angles[i];
            }
        }

        return best;
    }

    public string StateAt(float degrees)
    {
        for (var i = 0; i < angles.Length; i++)
        {
            if (Mathf.Abs(angles[i] - degrees) <= Tolerance)
            {
                return states[i];
            }
        }

        return null;
    }

    private int TickIndex(float degrees) => TickDegrees > 0f ? Mathf.FloorToInt(degrees / TickDegrees) : 0;
}
