using System.Collections.Generic;
using UnityEngine;

namespace Fieldmate.Interaction;

/// <summary>
/// Which grip point of a handle a pointer ray takes hold of (#86): the free point closest to the ray, so a ray grabs a
/// lever at its end, not at the pivot, and the second ray on the breaker bar takes the other end. No allocations.
/// </summary>
public static class GripChoice
{
    /// <summary>
    /// Index of the point in <paramref name="points"/> nearest to the ray from <paramref name="origin"/> along
    /// <paramref name="direction"/>, skipping indices marked in <paramref name="taken"/>; -1 when none is free.
    /// </summary>
    public static int Nearest(Vector3 origin, Vector3 direction, IReadOnlyList<Vector3> points, IReadOnlyList<bool> taken)
    {
        if (points == null)
        {
            return -1;
        }

        var dir = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;
        var best = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < points.Count; i++)
        {
            if (taken != null && i < taken.Count && taken[i])
            {
                continue;
            }

            var distance = DistanceToRay(origin, dir, points[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Distance from <paramref name="point"/> to the ray; points behind the origin measure to the origin.</summary>
    public static float DistanceToRay(Vector3 origin, Vector3 unitDirection, Vector3 point)
    {
        var toPoint = point - origin;
        var along = Vector3.Dot(toPoint, unitDirection);
        return along <= 0f ? toPoint.magnitude : (toPoint - unitDirection * along).magnitude;
    }
}
