using System;
using System.Collections.Generic;

namespace Fieldmate.Vision;

/// <summary>Why the camera can or can't capture right now, in words the user and the assistant can be given.</summary>
public enum CameraState
{
    /// <summary>The user turned on local-only mode: the camera isn't used and no image leaves the headset (#23).</summary>
    LocalOnly,

    /// <summary>The HEADSET_CAMERA permission hasn't been answered yet.</summary>
    WaitingForPermission,

    /// <summary>The user denied the permission; only the system settings can change that.</summary>
    Denied,

    /// <summary>Granted, but the camera hasn't delivered a frame yet (or there is no camera, e.g. in the editor).</summary>
    Starting,

    Ready,
}

/// <summary>Capture-size decisions for the passthrough camera (design.md §5.5: chosen, never hard-coded).</summary>
public static class CaptureSettings
{
    /// <summary>The vision model gets at most this long an edge (design.md §5.5 step 2).</summary>
    public const int UploadLongEdge = 640;

    /// <summary>Captures are on demand, never continuous: at most one per this many seconds.</summary>
    public const double MinSecondsBetweenCaptures = 1.0;

    /// <summary>
    /// The camera configuration to run: the smallest whose long edge still covers <paramref name="minLongEdge"/>
    /// (larger only costs conversion time, and nearest-neighbour downsampling from far above aliases), or the largest
    /// when none does. -1 for an empty list.
    /// </summary>
    public static int PickConfiguration(IReadOnlyList<(int Width, int Height)> sizes, int minLongEdge = UploadLongEdge)
    {
        if (sizes == null)
        {
            throw new ArgumentNullException(nameof(sizes));
        }

        int best = -1, largest = -1;
        for (var i = 0; i < sizes.Count; i++)
        {
            var edge = LongEdge(sizes[i]);
            if (edge <= 0)
            {
                continue;
            }

            if (largest < 0 || edge > LongEdge(sizes[largest]))
            {
                largest = i;
            }

            if (edge >= minLongEdge && (best < 0 || edge < LongEdge(sizes[best])))
            {
                best = i;
            }
        }

        return best >= 0 ? best : largest;
    }

    /// <summary>Scales a size down so its long edge is at most <paramref name="longEdge"/>, keeping the aspect ratio.</summary>
    public static (int Width, int Height) Fit(int width, int height, int longEdge = UploadLongEdge)
    {
        if (width <= 0 || height <= 0 || longEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Sizes must be positive.");
        }

        var edge = Math.Max(width, height);
        if (edge <= longEdge)
        {
            return (width, height);
        }

        var scale = (double)longEdge / edge;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>Why "What's this?" can't run, for the assistant to pass on; null when it can.</summary>
    public static string Explain(CameraState state) => state switch
    {
        CameraState.LocalOnly => "Local-only mode is on, so the camera isn't used and no image leaves the headset. It can be turned off with the Camera button in the hand menu.",
        CameraState.WaitingForPermission => "The headset is still asking for camera access.",
        CameraState.Denied => "Camera access was denied. It can be allowed in the headset's Settings, under Apps, Permissions.",
        CameraState.Starting => "The camera isn't delivering images yet.",
        _ => null,
    };

    /// <summary>True when a capture at <paramref name="now"/> keeps to <see cref="MinSecondsBetweenCaptures"/>.</summary>
    public static bool MayCapture(double now, double lastCapture) =>
        double.IsNegativeInfinity(lastCapture) || now - lastCapture >= MinSecondsBetweenCaptures;

    private static int LongEdge((int Width, int Height) size) => Math.Max(size.Width, size.Height);
}
