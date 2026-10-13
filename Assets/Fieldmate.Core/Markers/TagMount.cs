using UnityEngine;

namespace Fieldmate.Markers;

/// <summary>Where a machine's tag is fixed.</summary>
public enum TagMountKind
{
    /// <summary>On the floor where the machine stands, read upright from the machine's front.</summary>
    Floor = 0,

    /// <summary>On a vertical face of the machine (a nameplate), facing out of the machine's front.</summary>
    Wall = 1,
}

/// <summary>From a tag's pose in the room to the machine's pose (#27). The machine always stands upright.</summary>
public static class TagMount
{
    /// <param name="tag">The tag's world pose (<see cref="MarkerPose"/>: local z into the surface, local y the tag's top).</param>
    /// <param name="offset">From the tag to the machine's origin, in the machine's frame (x right, y up, z forward).</param>
    /// <param name="yawDegrees">Extra turn about the vertical, for machines whose front isn't where the tag is read from.</param>
    public static Pose MachinePose(Pose tag, TagMountKind kind, Vector3 offset, float yawDegrees, float floorClearance)
    {
        // The direction the machine's front faces: towards whoever reads the tag.
        var facing = kind == TagMountKind.Floor
            ? -(tag.rotation * Vector3.up)      // the reader stands at the tag's bottom edge
            : -(tag.rotation * Vector3.forward); // out of the surface the tag is on
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f)
        {
            facing = Vector3.forward;
        }

        var rotation = Quaternion.LookRotation(facing.normalized, Vector3.up) * Quaternion.Euler(0f, yawDegrees, 0f);
        var position = tag.position + rotation * offset;
        if (kind == TagMountKind.Floor)
        {
            position.y = tag.position.y + floorClearance;
        }

        return new Pose(position, rotation);
    }
}
