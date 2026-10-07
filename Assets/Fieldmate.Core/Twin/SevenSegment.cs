using System;

namespace Fieldmate.Twin;

/// <summary>
/// Seven-segment encoding for the gauge's digital window (#88). Segment bits: 0 a (top), 1 b (upper right),
/// 2 c (lower right), 3 d (bottom), 4 e (lower left), 5 f (upper left), 6 g (middle).
/// </summary>
public static class SevenSegment
{
    public const int Segments = 7;

    /// <summary>Digits shown: tens, ones and tenths ("10.0", " 6.8", " 0.0").</summary>
    public const int Digits = 3;

    private static readonly byte[] Patterns = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

    public static byte Mask(int digit) =>
        digit is >= 0 and <= 9 ? Patterns[digit] : throw new ArgumentOutOfRangeException(nameof(digit));

    public static bool IsLit(byte mask, int segment) => (mask & (1 << segment)) != 0;

    /// <summary>
    /// Fills <paramref name="masks"/> (tens, ones, tenths) for <paramref name="value"/> rounded to a tenth and clamped
    /// to 0–99.9; a leading zero in the tens is left dark. Returns the value shown, in tenths.
    /// </summary>
    public static int Tenths(float value, byte[] masks)
    {
        if (masks == null || masks.Length < Digits)
        {
            throw new ArgumentException($"Needs {Digits} masks.", nameof(masks));
        }

        var tenths = (int)Math.Round(Math.Max(0f, Math.Min(99.9f, value)) * 10f, MidpointRounding.AwayFromZero);
        var tens = tenths / 100;
        masks[0] = tens == 0 ? (byte)0 : Mask(tens);
        masks[1] = Mask(tenths / 10 % 10);
        masks[2] = Mask(tenths % 10);
        return tenths;
    }
}
