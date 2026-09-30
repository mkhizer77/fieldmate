namespace Fieldmate.Procedures;

/// <summary>
/// How long the gaze has rested on the same part. A short look elsewhere (head jitter, the ray slipping off a small
/// gauge onto the pipe beside it) is forgiven: the part stays current for <see cref="GraceSeconds"/> before the dwell
/// resets (device test 2026-09-30: the gauge dwell never reached 1.2 s).
/// </summary>
public sealed class DwellTracker
{
    public const float GraceSeconds = 0.4f;

    private float awaySeconds;

    public string PartId { get; private set; }
    public float Seconds { get; private set; }

    /// <summary>Feeds one frame: the part under the gaze (null for none) and the frame time. Returns the dwell.</summary>
    public float Update(string partId, float deltaSeconds)
    {
        if (partId == PartId)
        {
            awaySeconds = 0f;
            if (partId != null)
            {
                Seconds += deltaSeconds;
            }

            return Seconds;
        }

        if (PartId != null && awaySeconds < GraceSeconds)
        {
            awaySeconds += deltaSeconds; // still counts as the same look
            return Seconds;
        }

        PartId = partId;
        Seconds = 0f;
        awaySeconds = 0f;
        return Seconds;
    }

    public void Reset()
    {
        PartId = null;
        Seconds = 0f;
        awaySeconds = 0f;
    }
}
