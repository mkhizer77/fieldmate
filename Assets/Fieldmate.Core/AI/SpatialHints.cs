using System.Globalization;
using UnityEngine;

namespace Fieldmate.AI;

/// <summary>Where a part is relative to the user's head, in words the assistant can say: "1.6 m away, to your right".</summary>
public static class SpatialHints
{
    public static string Describe(Vector3 headPosition, Vector3 headForward, Vector3 target)
    {
        var to = target - headPosition;
        var flat = new Vector3(to.x, 0f, to.z);
        var forward = new Vector3(headForward.x, 0f, headForward.z);
        var distance = to.magnitude;
        var c = CultureInfo.InvariantCulture;
        if (flat.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-4f)
        {
            return string.Format(c, "{0:0.0} m away", distance);
        }

        var angle = Vector3.SignedAngle(forward.normalized, flat.normalized, Vector3.up);
        var abs = Mathf.Abs(angle);
        var side = angle < 0 ? "left" : "right";
        string direction = abs < 25f ? "straight ahead"
            : abs < 70f ? $"ahead to your {side}"
            : abs < 115f ? $"to your {side}"
            : abs < 155f ? $"behind you to the {side}"
            : "behind you";
        var vertical = to.y > 0.4f ? ", above eye level" : to.y < -0.5f ? ", low down" : string.Empty;
        return string.Format(c, "{0:0.0} m away, {1}{2}", distance, direction, vertical);
    }
}
