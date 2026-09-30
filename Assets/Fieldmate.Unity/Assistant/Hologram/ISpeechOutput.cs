namespace Fieldmate.Assistant;

/// <summary>The speech the user is hearing, as the hologram's lips need it (#69).</summary>
public interface ISpeechOutput
{
    bool IsPlaying { get; }

    /// <summary>Loudness of the audio leaving the speaker right now (RMS of the latest output block).</summary>
    float OutputRms();
}
