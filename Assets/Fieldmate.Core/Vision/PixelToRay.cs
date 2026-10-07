using UnityEngine;

namespace Fieldmate.Vision;

/// <summary>
/// Where the passthrough camera sits relative to the head. The Unity OpenXR Meta route exposes no camera extrinsics
/// (ADR-001), so the capture-time head pose plus this fixed offset stands in for the camera pose.
/// </summary>
public readonly struct CameraMount
{
    public CameraMount(Vector3 offset, Quaternion rotation)
    {
        Offset = offset;
        Rotation = rotation;
    }

    /// <summary>
    /// Quest 3 left RGB camera, approximately: 3.2 cm left of the head's centre, 2 cm below and 6 cm in front of the
    /// eyes, looking where the head looks. Calibrate on the headset (#22) before trusting it to the centimetre.
    /// </summary>
    public static CameraMount Quest3Left => new(new Vector3(-0.032f, -0.02f, 0.06f), Quaternion.identity);

    /// <summary>Position from the head's centre, in head space (metres).</summary>
    public Vector3 Offset { get; }

    /// <summary>Camera orientation relative to the head.</summary>
    public Quaternion Rotation { get; }

    public Pose CameraPose(Pose head) => new(head.position + head.rotation * Offset, head.rotation * Normalized(Rotation));

    // default(Quaternion) is all zeros; treat it as identity so a default mount is "camera at the head".
    private static Quaternion Normalized(Quaternion q) =>
        q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f ? Quaternion.identity : q.normalized;
}

/// <summary>
/// Turns camera pixels into world rays and back (design.md §5.5 step 4). Camera space is Unity's: x right, y up,
/// z forward; pixels have a top-left origin with y down.
/// </summary>
public static class PixelToRay
{
    public static Ray FromPixel(Vector2 pixel, CameraIntrinsics intrinsics, Pose camera)
    {
        var direction = new Vector3(
            (pixel.x - intrinsics.PrincipalPoint.x) / intrinsics.FocalLength.x,
            -(pixel.y - intrinsics.PrincipalPoint.y) / intrinsics.FocalLength.y,
            1f);
        return new Ray(camera.position, camera.rotation * direction);
    }

    /// <summary>A point in [0, 1]² of the image (how the vision model reports boxes) to a world ray.</summary>
    public static Ray FromNormalized(Vector2 normalized, CameraIntrinsics intrinsics, Pose camera) =>
        FromPixel(intrinsics.ToPixel(normalized), intrinsics, camera);

    /// <summary>
    /// Projects a world point into the image. False when the point is behind the camera; a pixel outside the image is
    /// still returned, check it with <see cref="CameraIntrinsics.Contains"/>.
    /// </summary>
    public static bool TryToPixel(Vector3 world, CameraIntrinsics intrinsics, Pose camera, out Vector2 pixel)
    {
        var local = Quaternion.Inverse(camera.rotation) * (world - camera.position);
        if (local.z <= 1e-4f)
        {
            pixel = default;
            return false;
        }

        pixel = new Vector2(
            intrinsics.PrincipalPoint.x + intrinsics.FocalLength.x * local.x / local.z,
            intrinsics.PrincipalPoint.y - intrinsics.FocalLength.y * local.y / local.z);
        return true;
    }

    /// <summary>
    /// The pixel the user was looking at: where the gaze ray, followed out to <paramref name="distance"/> metres, lands in
    /// the image. The camera sits a few centimetres off the eyes, so the result depends on that distance (parallax).
    /// </summary>
    public static bool TryGazePixel(Ray gaze, float distance, CameraIntrinsics intrinsics, Pose camera, out Vector2 pixel) =>
        TryToPixel(gaze.GetPoint(distance), intrinsics, camera, out pixel) && intrinsics.Contains(pixel);
}
