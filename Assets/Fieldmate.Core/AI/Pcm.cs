using System;

namespace Fieldmate.AI;

/// <summary>16-bit PCM helpers for the speech providers: WAV encoding for speech-to-text, streaming decode for TTS.</summary>
public static class Pcm
{
    public const int WavHeaderBytes = 44;

    /// <summary>Encodes mono samples as a 16-bit PCM WAV file.</summary>
    public static byte[] EncodeWav(AudioData audio)
    {
        if (audio == null)
        {
            throw new ArgumentNullException(nameof(audio));
        }

        var samples = audio.Samples;
        var dataBytes = samples.Length * 2;
        var bytes = new byte[WavHeaderBytes + dataBytes];
        WriteAscii(bytes, 0, "RIFF");
        WriteInt(bytes, 4, 36 + dataBytes);
        WriteAscii(bytes, 8, "WAVE");
        WriteAscii(bytes, 12, "fmt ");
        WriteInt(bytes, 16, 16); // fmt chunk size
        WriteShort(bytes, 20, 1); // PCM
        WriteShort(bytes, 22, 1); // mono
        WriteInt(bytes, 24, audio.SampleRate);
        WriteInt(bytes, 28, audio.SampleRate * 2); // byte rate
        WriteShort(bytes, 32, 2); // block align
        WriteShort(bytes, 34, 16); // bits per sample
        WriteAscii(bytes, 36, "data");
        WriteInt(bytes, 40, dataBytes);

        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i];
            value = value > 1f ? 1f : value < -1f ? -1f : value;
            WriteShort(bytes, WavHeaderBytes + i * 2, (short)Math.Round(value * short.MaxValue));
        }

        return bytes;
    }

    private static void WriteAscii(byte[] bytes, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            bytes[offset + i] = (byte)text[i];
        }
    }

    private static void WriteInt(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteShort(byte[] bytes, int offset, short value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }
}

/// <summary>
/// Turns a stream of little-endian 16-bit PCM chunks into float samples. Chunks may split a sample across calls; the
/// odd byte is carried over. Reuses one buffer, so steady-state decoding allocates nothing.
/// </summary>
public sealed class Pcm16StreamDecoder
{
    private float[] buffer = new float[2048];
    private byte carry;
    private bool hasCarry;

    /// <summary>Total samples produced so far.</summary>
    public long SamplesDecoded { get; private set; }

    /// <summary>Decodes <paramref name="count"/> bytes and reports the samples; the buffer passed out is reused.</summary>
    public void Decode(byte[] data, int count, Action<float[], int> onSamples)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (count < 0 || count > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var total = count + (hasCarry ? 1 : 0);
        var samples = total / 2;
        if (samples > buffer.Length)
        {
            buffer = new float[Math.Max(samples, buffer.Length * 2)];
        }

        var index = 0;
        var produced = 0;
        if (hasCarry && count > 0)
        {
            buffer[produced++] = ToFloat(carry, data[0]);
            index = 1;
            hasCarry = false;
        }

        for (; index + 1 < count; index += 2)
        {
            buffer[produced++] = ToFloat(data[index], data[index + 1]);
        }

        if (index < count)
        {
            carry = data[index];
            hasCarry = true;
        }

        if (produced > 0)
        {
            SamplesDecoded += produced;
            onSamples?.Invoke(buffer, produced);
        }
    }

    public void Reset()
    {
        hasCarry = false;
        SamplesDecoded = 0;
    }

    private static float ToFloat(byte low, byte high) => (short)(low | (high << 8)) / 32768f;
}
