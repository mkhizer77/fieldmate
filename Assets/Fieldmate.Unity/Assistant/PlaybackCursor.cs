using System.Threading;

namespace Fieldmate.Assistant;

/// <summary>
/// Knows where the last real speech sample sits in the played stream. Unity pulls samples from a streaming clip ahead
/// of playback (how far is not reported reliably), so "ring empty" only means the audio thread has the last samples.
/// The stream plays continuously at its sample rate — underruns are filled with silence, which counts — so the last
/// real sample is heard <see cref="LastRealEndSeconds"/> after playback started. The audio thread reports each read.
/// </summary>
public sealed class PlaybackCursor
{
    private long delivered;     // samples handed to Unity since Reset, real + silence (audio thread)
    private long lastRealEnd;   // stream position just after the last real sample (audio thread)

    public long LastRealEnd => Interlocked.Read(ref lastRealEnd);

    /// <summary>Seconds of stream, from the start of playback, until the last real sample has played.</summary>
    public double LastRealEndSeconds(int sampleRate) => sampleRate > 0 ? (double)LastRealEnd / sampleRate : 0d;

    public void Reset()
    {
        Interlocked.Exchange(ref delivered, 0);
        Interlocked.Exchange(ref lastRealEnd, 0);
    }

    /// <summary>Audio thread: <paramref name="length"/> samples were handed over, the first <paramref name="real"/> of them audio.</summary>
    public void OnRead(int length, int real)
    {
        var start = Interlocked.Add(ref delivered, length) - length;
        if (real > 0)
        {
            Interlocked.Exchange(ref lastRealEnd, start + real);
        }
    }
}
