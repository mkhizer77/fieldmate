using System;
using System.Text;

namespace Fieldmate.Net;

/// <summary>Little-endian binary writer over a reusable buffer: no allocations once the buffer has grown.</summary>
public sealed class NetWriter
{
    private byte[] buffer;

    public NetWriter(int capacity = 512) => buffer = new byte[Math.Max(16, capacity)];

    public int Length { get; private set; }
    public byte[] Buffer => buffer;
    public ArraySegment<byte> Segment => new(buffer, 0, Length);

    public NetWriter Reset()
    {
        Length = 0;
        return this;
    }

    public void Byte(byte value)
    {
        Ensure(1);
        buffer[Length++] = value;
    }

    public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    public void UShort(ushort value)
    {
        Ensure(2);
        buffer[Length++] = (byte)value;
        buffer[Length++] = (byte)(value >> 8);
    }

    public void Short(short value) => UShort(unchecked((ushort)value));

    public void UInt(uint value)
    {
        Ensure(4);
        buffer[Length++] = (byte)value;
        buffer[Length++] = (byte)(value >> 8);
        buffer[Length++] = (byte)(value >> 16);
        buffer[Length++] = (byte)(value >> 24);
    }

    public void Int(int value) => UInt(unchecked((uint)value));

    public void Float(float value) => Int(BitConverter.SingleToInt32Bits(value));

    /// <summary>UTF-8 with a 16-bit byte length; longer strings are cut at <paramref name="maxChars"/>.</summary>
    public void String(string value, int maxChars = 1000)
    {
        value ??= string.Empty;
        if (value.Length > maxChars)
        {
            value = value.Substring(0, maxChars);
        }

        var count = Encoding.UTF8.GetByteCount(value);
        UShort((ushort)count);
        Ensure(count);
        Length += Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, Length);
    }

    private void Ensure(int extra)
    {
        if (Length + extra <= buffer.Length)
        {
            return;
        }

        Array.Resize(ref buffer, Math.Max(buffer.Length * 2, Length + extra));
    }
}

/// <summary>Reads what <see cref="NetWriter"/> wrote. Any overrun throws <see cref="FormatException"/>.</summary>
public sealed class NetReader
{
    private readonly byte[] buffer;
    private readonly int end;
    private int position;

    public NetReader(ArraySegment<byte> data)
    {
        buffer = data.Array ?? Array.Empty<byte>();
        position = data.Offset;
        end = data.Offset + data.Count;
    }

    public bool AtEnd => position >= end;

    public byte Byte()
    {
        Need(1);
        return buffer[position++];
    }

    public bool Bool() => Byte() != 0;

    public ushort UShort()
    {
        Need(2);
        var value = (ushort)(buffer[position] | (buffer[position + 1] << 8));
        position += 2;
        return value;
    }

    public short Short() => unchecked((short)UShort());

    public uint UInt()
    {
        Need(4);
        var value = (uint)(buffer[position] | (buffer[position + 1] << 8) | (buffer[position + 2] << 16) | (buffer[position + 3] << 24));
        position += 4;
        return value;
    }

    public int Int() => unchecked((int)UInt());

    public float Float() => BitConverter.Int32BitsToSingle(Int());

    public string String()
    {
        var count = UShort();
        Need(count);
        var value = Encoding.UTF8.GetString(buffer, position, count);
        position += count;
        return value;
    }

    private void Need(int count)
    {
        if (position + count > end)
        {
            throw new FormatException("Message ended early.");
        }
    }
}
