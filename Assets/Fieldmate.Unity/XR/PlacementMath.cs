using UnityEngine;

namespace Fieldmate.XR;

/// <summary>Pure geometry for placing the machine on the floor (testable without XR).</summary>
public static class PlacementMath
{
    /// <summary>Surfaces flatter than this (normal·up) count as floor.</summary>
    public const float MinUpDot = 0.85f;

    /// <summary>Furthest placement distance from the pointer origin, metres.</summary>
    public const float MaxDistance = 6f;

    public static bool IsFloorLike(Vector3 normal) => Vector3.Dot(normal.normalized, Vector3.up) >= MinUpDot;

    /// <summary>
    /// Where the ray meets the horizontal plane at <paramref name="floorHeight"/>; false when it points upwards, starts
    /// below the floor or hits beyond <see cref="MaxDistance"/>. Used when no room scan is available.
    /// </summary>
    public static bool TryHitFloorPlane(Ray ray, float floorHeight, out Vector3 point)
    {
        point = default;
        var direction = ray.direction.normalized;
        if (direction.y > -0.05f || ray.origin.y <= floorHeight)
        {
            return false;
        }

        var distance = (ray.origin.y - floorHeight) / -direction.y;
        if (distance > MaxDistance)
        {
            return false;
        }

        point = ray.origin + direction * distance;
        point.y = floorHeight;
        return true;
    }

    /// <summary>Upright rotation turning the machine's front (+Z) towards the viewer, plus a user-chosen yaw.</summary>
    public static Quaternion FacingViewer(Vector3 machinePosition, Vector3 viewerPosition, float extraYawDegrees)
    {
        var toViewer = Vector3.ProjectOnPlane(viewerPosition - machinePosition, Vector3.up);
        var baseRotation = toViewer.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toViewer, Vector3.up) : Quaternion.identity;
        return baseRotation * Quaternion.Euler(0f, extraYawDegrees, 0f);
    }

    /// <summary>Keeps a yaw in (-180, 180].</summary>
    public static float WrapYaw(float degrees)
    {
        degrees %= 360f;
        return degrees > 180f ? degrees - 360f : degrees <= -180f ? degrees + 360f : degrees;
    }
}
