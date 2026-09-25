using System;

namespace Fieldmate.Assistant;

/// <summary>
/// Single-producer / single-consumer float ring buffer between the network thread's TTS chunks and Unity's audio
/// thread. Fixed capacity, no allocations after construction; missing samples read as silence.
/// </summary>
public sealed class AudioRingBuffer
{
    private readonly float[] buffer;
    private readonly object gate = new();
    private int read;
    private int count;

    public AudioRingBuffer(int capacity) =>
        buffer = new float[capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];

    public int Capacity => buffer.Length;

    public int Count
    {
        get
        {
            lock (gate)
            {
                return count;
            }
        }
    }

    /// <summary>Appends samples; returns how many fit (the rest are dropped rather than overwriting unplayed audio).</summary>
    public int Write(float[] samples, int length)
    {
        lock (gate)
        {
            var writable = Math.Min(length, buffer.Length - count);
            var write = (read + count) % buffer.Length;
            for (var i = 0; i < writable; i++)
            {
                buffer[write] = samples[i];
                write = write + 1 == buffer.Length ? 0 : write + 1;
            }

            count += writable;
            return writable;
        }
    }

    /// <summary>Fills <paramref name="output"/>; returns the number of real samples (the rest is silence).</summary>
    public int Read(float[] output)
    {
        lock (gate)
        {
            var readable = Math.Min(output.Length, count);
            for (var i = 0; i < readable; i++)
            {
                output[i] = buffer[read];
                read = read + 1 == buffer.Length ? 0 : read + 1;
            }

            for (var i = readable; i < output.Length; i++)
            {
                output[i] = 0f;
            }

            count -= readable;
            return readable;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            read = 0;
            count = 0;
        }
    }
}
