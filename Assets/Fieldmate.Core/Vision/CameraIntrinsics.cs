using System;
using UnityEngine;

namespace Fieldmate.Vision;

/// <summary>
/// Pinhole intrinsics of one passthrough camera image, in pixels with the origin at the image's top-left corner and y
/// pointing down (the way the uploaded JPEG and the vision model's boxes see it).
/// </summary>
public readonly struct CameraIntrinsics
{
    public CameraIntrinsics(Vector2 focalLength, Vector2 principalPoint, int width, int height)
    {
        if (focalLength.x <= 0f || focalLength.y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(focalLength), "Focal lengths must be positive.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The image needs a size.");
        }

        FocalLength = focalLength;
        PrincipalPoint = principalPoint;
        Width = width;
        Height = height;
    }

    public Vector2 FocalLength { get; }
    public Vector2 PrincipalPoint { get; }
    public int Width { get; }
    public int Height { get; }

    public bool IsValid => Width > 0 && Height > 0 && FocalLength.x > 0f && FocalLength.y > 0f;

    /// <summary>Horizontal field of view in degrees.</summary>
    public float HorizontalFov => 2f * Mathf.Atan(Width * 0.5f / FocalLength.x) * Mathf.Rad2Deg;

    /// <summary>Converts a point in [0, 1]² (top-left origin) to a pixel of this image.</summary>
    public Vector2 ToPixel(Vector2 normalized) => new(normalized.x * Width, normalized.y * Height);

    public bool Contains(Vector2 pixel) => pixel.x >= 0f && pixel.y >= 0f && pixel.x <= Width && pixel.y <= Height;
}
