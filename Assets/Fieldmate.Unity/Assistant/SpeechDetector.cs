using System;

namespace Fieldmate.Assistant;

/// <summary>
/// Minimal voice-activity check on microphone level (RMS), fed once per frame: speech is declared once the level has
/// been above <see cref="Threshold"/> for <see cref="MinVoicedSeconds"/> (quiet frames decay it slowly, so short gaps
/// between syllables don't reset it). Deliberately conservative: the assistant's own voice leaks into the microphone
/// (reply is ducked while listening), and a missed detection only costs latency, because a long press still goes to
/// speech-to-text. Tuned on Quest 3 (2026-09-28): silence and the assistant's own voice read 0.000 (the headset
/// cancels its echo), calm speech peaks 0.033–0.054, raised speech 0.083.
/// </summary>
public sealed class SpeechDetector
{
    public const float Threshold = 0.012f;

    /// <summary>Below this peak a recording is silence (device reads 0.000); it isn't sent to speech-to-text.</summary>
    public const float SilencePeak = 0.005f;
    public const float MinVoicedSeconds = 0.2f;

    private float voiced;

    /// <summary>Highest level seen since <see cref="Reset"/> (for logs and tuning).</summary>
    public float Peak { get; private set; }

    /// <summary>Adds one frame's level; true once speech is detected.</summary>
    public bool Add(float level, float deltaSeconds)
    {
        Peak = Math.Max(Peak, level);
        voiced = level >= Threshold ? voiced + deltaSeconds : Math.Max(0f, voiced - deltaSeconds * 0.5f);
        return voiced >= MinVoicedSeconds;
    }

    public void Reset()
    {
        voiced = 0f;
        Peak = 0f;
    }
}
