using UnityEngine;

namespace Fieldmate.XR;

/// <summary>
/// Where the hologram mate goes while the user points it into place (#71): onto a table, shelf or counter the ray hits,
/// otherwise floating along the ray at a comfortable distance and height, so its face ends up a little below the
/// user's eyes and never on the floor or above their head.
/// </summary>
public static class MatePlacementMath
{
    public const float MaxSurfaceDistance = 2.5f;
    public const float MinSurfaceHeight = 0.4f; // a table or shelf, not the floor
    public const float DefaultDistance = 1f;
    public const float MinDistance = 0.6f;
    public const float MaxDistance = 1.6f;
    public const float LowestBelowEyes = 0.75f;
    public const float HighestBelowEyes = 0.25f;

    /// <summary>The puck position for a pointing ray, given what (if anything) the ray hit.</summary>
    public static Vector3 Target(Ray ray, bool hit, Vector3 hitPoint, Vector3 hitNormal, float headY)
    {
        var distance = hit ? Vector3.Distance(ray.origin, hitPoint) : float.PositiveInfinity;
        if (hit && distance <= MaxSurfaceDistance && hitNormal.y > 0.8f && hitPoint.y >= MinSurfaceHeight)
        {
            return hitPoint; // stands on the surface
        }

        var along = hit ? Mathf.Clamp(distance - 0.15f, MinDistance, MaxDistance) : DefaultDistance;
        var p = ray.origin + ray.direction.normalized * along;
        p.y = Mathf.Clamp(p.y, Mathf.Max(0.5f, headY - LowestBelowEyes), headY - HighestBelowEyes);
        return p;
    }
}
