using UnityEngine;

namespace Fieldmate.XR;

/// <summary>
/// Picks the machine's size from the free floor around where it is being placed (#57): the largest scale between
/// <see cref="MinScale"/> and <see cref="MaxScale"/> at which the machine's footprint, plus room to work in front and to
/// pass at the sides, fits inside the free distances measured in each direction. Pure, so it is tested without a room.
/// Directions and the footprint are in the machine's own frame (+Z is its front, towards the user).
/// </summary>
public static class MachineFit
{
    public const float MinScale = 0.6f;
    public const float MaxScale = 1f;

    /// <summary>The full-size skid's footprint around its pivot: cabinet end to frame end, back to the step card.</summary>
    public static readonly Rect Footprint = Rect.MinMaxRect(-1.45f, -0.36f, 0.85f, 0.4f);

    /// <summary>Room to keep free: in front to stand and work, at the sides to pass, behind (it may stand near a wall).</summary>
    public const float FrontClearance = 0.9f;
    public const float SideClearance = 0.35f;
    public const float BackClearance = 0.15f;

    /// <summary>
    /// Scale at which no obstacle point (where a probe ray hit the room, in the machine's local XZ) falls inside the
    /// footprint grown by the clearances. No points: full size. <paramref name="tight"/>: even the minimum doesn't fit.
    /// </summary>
    public static float Scale(Vector2[] obstacles, int count, out bool tight)
    {
        var best = float.PositiveInfinity;
        for (var i = 0; i < count; i++)
        {
            best = Mathf.Min(best, LargestClear(obstacles[i]));
        }

        tight = best < MinScale;
        return Mathf.Clamp(float.IsPositiveInfinity(best) ? MaxScale : best, MinScale, MaxScale);
    }

    /// <summary>Unit direction <paramref name="i"/> of <paramref name="count"/> around the machine, in its local XZ plane.</summary>
    public static Vector2 Direction(int i, int count)
    {
        var a = i * Mathf.PI * 2f / count;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    // The largest scale that keeps p outside the grown footprint: p must clear at least one of its four sides.
    private static float LargestClear(Vector2 p)
    {
        var right = (p.x - SideClearance) / Footprint.xMax;
        var left = (-p.x - SideClearance) / -Footprint.xMin;
        var front = (p.y - FrontClearance) / Footprint.yMax;
        var back = (-p.y - BackClearance) / -Footprint.yMin;
        return Mathf.Max(Mathf.Max(right, left), Mathf.Max(front, back));
    }
}
