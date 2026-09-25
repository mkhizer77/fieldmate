using System.Threading;

namespace Fieldmate.Assistant;

/// <summary>
/// Decides when streamed speech has actually been heard. Unity pulls samples from a streaming clip ahead of the
/// playhead, so an empty ring buffer only means the audio thread has the last samples, not that they were played.
/// The audio thread reports what it handed over (<see cref="OnRead"/>); the main thread reports the playhead
/// (<see cref="Advance"/>); speech is finished once the playhead passes the last real sample. No allocations.
/// </summary>
public sealed class PlaybackCursor
{
    private readonly int clipLength;
    private long delivered;     // samples handed to Unity since Reset, real + silence (audio thread)
    private long lastRealEnd;   // stream position just after the last real sample (audio thread)
    private long played;        // samples the playhead has advanced since Reset (main thread)
    private int lastPosition;

    public PlaybackCursor(int clipLength) => this.clipLength = clipLength;

    public long Played => played;
    public long LastRealEnd => Interlocked.Read(ref lastRealEnd);

    /// <summary>True once everything real that was handed to Unity has passed the playhead.</summary>
    public bool Drained => played >= LastRealEnd;

    public void Reset()
    {
        Interlocked.Exchange(ref delivered, 0);
        Interlocked.Exchange(ref lastRealEnd, 0);
        played = 0;
        lastPosition = 0;
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

    /// <summary>Main thread: the looping clip's playhead is at <paramref name="position"/> (wraps at the clip length).</summary>
    public void Advance(int position)
    {
        var delta = position - lastPosition;
        if (delta < 0)
        {
            delta += clipLength;
        }

        played += delta;
        lastPosition = position;
    }
}
