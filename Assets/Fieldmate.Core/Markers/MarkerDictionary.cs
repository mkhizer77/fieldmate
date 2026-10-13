using System;
using System.Collections.Generic;

namespace Fieldmate.Markers;

/// <summary>
/// Fieldmate machine tags (#27): square fiducials of 7×7 cells, a one-cell black border around a 5×5 grid of data
/// cells (black = 1), printed with a one-cell white margin. The codewords are chosen so that any two, in any of their
/// four rotations, differ in at least <see cref="MinDistance"/> cells: a tag reads correctly with up to two wrong cells,
/// and its rotation is never ambiguous. Generated deterministically, so printed tags and the app always agree.
/// </summary>
public static class MarkerDictionary
{
    public const int DataCells = 5;
    public const int GridCells = DataCells + 2;
    public const int Count = 32;
    public const int MinDistance = 6;

    /// <summary>Cells that may be wrong and still decode: (MinDistance - 1) / 2.</summary>
    public const int MaxCorrectable = (MinDistance - 1) / 2;

    private static readonly uint[] Words = Generate();

    /// <summary>The 25 data bits of tag <paramref name="id"/>, row-major from the top-left cell (bit 24 first).</summary>
    public static uint Code(int id) =>
        id >= 0 && id < Count ? Words[id] : throw new ArgumentOutOfRangeException(nameof(id), $"Tags are 0..{Count - 1}.");

    /// <summary>True when cell (row, column) of the 5×5 data grid of tag <paramref name="id"/> is black.</summary>
    public static bool Cell(int id, int row, int column) => Bit(Code(id), row, column);

    /// <summary>
    /// The tag a read grid most likely is. <paramref name="rotation"/> is how many quarter turns clockwise the read grid
    /// is from upright; <paramref name="errors"/> how many cells disagreed. False when nothing is close enough.
    /// </summary>
    public static bool TryMatch(uint read, out int id, out int rotation, out int errors)
    {
        id = -1;
        rotation = 0;
        errors = int.MaxValue;
        var turned = read;
        for (var r = 0; r < 4; r++)
        {
            for (var i = 0; i < Words.Length; i++)
            {
                var d = Distance(turned, Words[i]);
                if (d < errors)
                {
                    errors = d;
                    id = i;
                    rotation = r;
                }
            }

            turned = RotateCounterClockwise(turned);
        }

        return errors <= MaxCorrectable;
    }

    /// <summary>The grid turned a quarter turn counter-clockwise (what was the right column becomes the top row).</summary>
    public static uint RotateCounterClockwise(uint code)
    {
        uint result = 0;
        for (var row = 0; row < DataCells; row++)
        {
            for (var column = 0; column < DataCells; column++)
            {
                if (Bit(code, row, column))
                {
                    // (row, column) → (DataCells - 1 - column, row)
                    result |= Mask(DataCells - 1 - column, row);
                }
            }
        }

        return result;
    }

    public static int Distance(uint a, uint b)
    {
        var x = a ^ b;
        var count = 0;
        while (x != 0)
        {
            x &= x - 1;
            count++;
        }

        return count;
    }

    public static bool Bit(uint code, int row, int column) => (code & Mask(row, column)) != 0;

    public static uint Mask(int row, int column) => 1u << (DataCells * DataCells - 1 - (row * DataCells + column));

    private static uint[] Generate()
    {
        var accepted = new List<uint>(Count);
        var state = 0x2545F491u; // xorshift32 seed: fixed forever, or printed tags stop matching
        while (accepted.Count < Count)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var candidate = state & ((1u << (DataCells * DataCells)) - 1);
            var ones = Distance(candidate, 0);
            if (ones < 9 || ones > 16)
            {
                continue; // mostly-black or mostly-white grids read poorly
            }

            var rotations = new uint[4];
            rotations[0] = candidate;
            for (var r = 1; r < 4; r++)
            {
                rotations[r] = RotateCounterClockwise(rotations[r - 1]);
            }

            var ok = true;
            for (var r = 1; r < 4 && ok; r++)
            {
                ok = Distance(candidate, rotations[r]) >= MinDistance;
            }

            foreach (var word in accepted)
            {
                for (var r = 0; r < 4 && ok; r++)
                {
                    ok = Distance(word, rotations[r]) >= MinDistance;
                }

                if (!ok)
                {
                    break;
                }
            }

            if (ok)
            {
                accepted.Add(candidate);
            }
        }

        return accepted.ToArray();
    }
}
