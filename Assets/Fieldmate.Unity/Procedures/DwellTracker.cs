namespace Fieldmate.Procedures;

/// <summary>How long the gaze has rested on the same part; resets when it moves to another part or to nothing.</summary>
public sealed class DwellTracker
{
    public string PartId { get; private set; }
    public float Seconds { get; private set; }

    /// <summary>Feeds one frame: the part under the gaze (null for none) and the frame time. Returns the dwell.</summary>
    public float Update(string partId, float deltaSeconds)
    {
        if (partId != PartId)
        {
            PartId = partId;
            Seconds = 0f;
        }
        else if (partId != null)
        {
            Seconds += deltaSeconds;
        }

        return Seconds;
    }

    public void Reset()
    {
        PartId = null;
        Seconds = 0f;
    }
}
