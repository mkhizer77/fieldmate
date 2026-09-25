using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Fieldmate.XR;

/// <summary>One passthrough camera frame with the data needed to turn pixels into world rays.</summary>
public readonly struct CapturedFrame
{
    public CapturedFrame(Texture2D texture, XRCameraIntrinsics intrinsics, bool hasIntrinsics, Pose headPose, double timestampSeconds)
    {
        Texture = texture;
        Intrinsics = intrinsics;
        HasIntrinsics = hasIntrinsics;
        HeadPose = headPose;
        TimestampSeconds = timestampSeconds;
    }

    public Texture2D Texture { get; }
    public XRCameraIntrinsics Intrinsics { get; }
    public bool HasIntrinsics { get; }

    /// <summary>
    /// Head pose when the image was acquired. The Unity OpenXR Meta route exposes no camera extrinsics, so this is an
    /// approximation of the camera pose (see ADR-001).
    /// </summary>
    public Pose HeadPose { get; }

    public double TimestampSeconds { get; }
}

/// <summary>On-demand CPU capture of the latest passthrough image (left camera). Never call per frame.</summary>
public static class CameraFrameCapture
{
    public static bool TryCapture(ARCameraManager cameraManager, Transform head, out CapturedFrame frame, out string error)
    {
        frame = default;
        if (cameraManager == null || !cameraManager.enabled)
        {
            error = "camera manager disabled";
            return false;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
        {
            error = "no CPU image (camera permission, Camera Image Support, or subsystem not ready)";
            return false;
        }

        var headPose = new Pose(head.position, head.rotation);
        using (image)
        {
            var texture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
            // Quest 3 on device: MirrorY (AR Foundation's sample default) produced a 180-degree rotated image; MirrorX is
            // upright in both the texture and the PNG (see ADR-001).
            var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorX);
            image.Convert(conversion, texture.GetRawTextureData<byte>());
            texture.Apply();

            var hasIntrinsics = cameraManager.TryGetIntrinsics(out var intrinsics);
            frame = new CapturedFrame(texture, intrinsics, hasIntrinsics, headPose, image.timestamp);
        }

        error = null;
        return true;
    }
}
