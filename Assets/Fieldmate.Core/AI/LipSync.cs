using System;

namespace Fieldmate.AI;

/// <summary>
/// Mouth openness (0 closed – 1 wide) for the hologram mate (#69), from the loudness of the speech actually playing.
/// Amplitude lip-sync: below <see cref="NoiseFloor"/> the mouth is shut, at <see cref="FullOpen"/> RMS it is wide open,
/// with a perceptual curve in between. It opens fast (<see cref="AttackSeconds"/>) and closes a little slower
/// (<see cref="ReleaseSeconds"/>), so syllables read as separate movements without chattering. No allocations.
/// </summary>
public sealed class LipSync
{
    public LipSync(float noiseFloor = 0.008f, float fullOpen = 0.12f, float attackSeconds = 0.035f, float releaseSeconds = 0.08f)
    {
        if (fullOpen <= noiseFloor)
        {
            throw new ArgumentException("fullOpen must be above the noise floor.", nameof(fullOpen));
        }

        NoiseFloor = noiseFloor;
        FullOpen = fullOpen;
        AttackSeconds = Math.Max(1e-4f, attackSeconds);
        ReleaseSeconds = Math.Max(1e-4f, releaseSeconds);
    }

    public float NoiseFloor { get; }
    public float FullOpen { get; }
    public float AttackSeconds { get; }
    public float ReleaseSeconds { get; }

    /// <summary>Current openness, 0..1.</summary>
    public float Open { get; private set; }

    /// <summary>Where the mouth is heading for a given loudness, before smoothing.</summary>
    public float Target(float rms)
    {
        var t = (rms - NoiseFloor) / (FullOpen - NoiseFloor);
        if (t <= 0f)
        {
            return 0f;
        }

        return t >= 1f ? 1f : (float)Math.Pow(t, 0.6);
    }

    /// <summary>Moves towards the target for this frame's loudness; returns the new openness.</summary>
    public float Update(float rms, float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return Open;
        }

        var target = Target(rms);
        var tau = target > Open ? AttackSeconds : ReleaseSeconds;
        var k = 1f - (float)Math.Exp(-deltaSeconds / tau);
        Open += (target - Open) * k;
        if (Open < 1e-3f)
        {
            Open = 0f;
        }

        return Open;
    }

    /// <summary>Shuts the mouth at once (speech stopped or interrupted).</summary>
    public void Close() => Open = 0f;

    /// <summary>Root-mean-square of the first <paramref name="count"/> samples.</summary>
    public static float Rms(float[] samples, int count)
    {
        if (samples == null || count <= 0)
        {
            return 0f;
        }

        count = Math.Min(count, samples.Length);
        double sum = 0d;
        for (var i = 0; i < count; i++)
        {
            sum += samples[i] * samples[i];
        }

        return (float)Math.Sqrt(sum / count);
    }
}
