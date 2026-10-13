using Fieldmate.Vision;
using UnityEngine;

namespace Fieldmate.Markers;

/// <summary>
/// Where a tag is relative to the camera, from its four corners and its printed size (#27): the planar homography
/// between the tag and the normalised image, split into rotation and translation.
/// </summary>
public static class MarkerPose
{
    /// <summary>
    /// The tag's pose in camera space (Unity's: x right, y up, z forward). Position is the tag's centre; local x points
    /// along the tag's top edge to the right, local y up the tag, local z into the surface it's printed on.
    /// </summary>
    /// <param name="corners">The tag's top-left, top-right, bottom-right, bottom-left corners in pixels.</param>
    /// <param name="side">The printed side of the black square, in metres.</param>
    public static bool TryEstimate(Vector2[] corners, float side, CameraIntrinsics intrinsics, out Pose pose)
    {
        pose = default;
        if (corners is not { Length: 4 } || side <= 0f || !intrinsics.IsValid)
        {
            return false;
        }

        // A real tag projects to a convex quadrilateral with some area; anything else would give a nonsense pose.
        var turn = 0;
        for (var i = 0; i < 4; i++)
        {
            var e1 = corners[(i + 1) % 4] - corners[i];
            var e2 = corners[(i + 2) % 4] - corners[(i + 1) % 4];
            var cross = e1.x * e2.y - e1.y * e2.x;
            if (Mathf.Abs(cross) < 1f)
            {
                return false;
            }

            turn += cross > 0f ? 1 : -1;
        }

        if (Mathf.Abs(turn) != 4)
        {
            return false;
        }

        var half = side * 0.5f;
        var model = new[] { new Vector2(-half, half), new Vector2(half, half), new Vector2(half, -half), new Vector2(-half, -half) };
        var image = new Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            image[i] = new Vector2(
                (corners[i].x - intrinsics.PrincipalPoint.x) / intrinsics.FocalLength.x,
                -(corners[i].y - intrinsics.PrincipalPoint.y) / intrinsics.FocalLength.y);
        }

        Homography h;
        try
        {
            h = Homography.Solve(model, image);
        }
        catch (System.ArgumentException)
        {
            return false;
        }

        // H ~ [r1 r2 t]: the first two rotation columns and the translation, up to one scale.
        var h1 = new Vector3((float)h[0, 0], (float)h[1, 0], (float)h[2, 0]);
        var h2 = new Vector3((float)h[0, 1], (float)h[1, 1], (float)h[2, 1]);
        var h3 = new Vector3((float)h[0, 2], (float)h[1, 2], (float)h[2, 2]);
        var scale = 2f / (h1.magnitude + h2.magnitude);
        var t = h3 * scale;
        var r1 = h1 * scale;
        var r2 = h2 * scale;
        if (t.z < 0f)
        {
            t = -t;
            r1 = -r1;
            r2 = -r2;
        }

        if (t.z < 1e-3f)
        {
            return false;
        }

        r1.Normalize();
        r2 = (r2 - Vector3.Dot(r1, r2) * r1).normalized;
        var r3 = Vector3.Cross(r1, r2);
        pose = new Pose(t, Quaternion.LookRotation(r3, r2));
        return true;
    }

    /// <summary>A camera-space pose into the world, given the camera's pose at capture.</summary>
    public static Pose ToWorld(Pose inCamera, Pose camera) =>
        new(camera.position + camera.rotation * inCamera.position, camera.rotation * inCamera.rotation);
}
