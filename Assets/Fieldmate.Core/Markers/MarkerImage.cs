using System;

namespace Fieldmate.Markers;

/// <summary>A printable tag: 7×7 cells plus a one-cell white margin, as 8-bit gray, row 0 at the top.</summary>
public static class MarkerImage
{
    public const int CellsWithMargin = MarkerDictionary.GridCells + 2;

    public static byte[] Render(int id, int cellPixels, out int size)
    {
        if (cellPixels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cellPixels));
        }

        size = CellsWithMargin * cellPixels;
        var pixels = new byte[size * size];
        const int n = MarkerDictionary.GridCells;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var row = y / cellPixels - 1;
                var column = x / cellPixels - 1;
                var black = row >= 0 && column >= 0 && row < n && column < n &&
                            (row == 0 || column == 0 || row == n - 1 || column == n - 1 || MarkerDictionary.Cell(id, row - 1, column - 1));
                pixels[y * size + x] = black ? (byte)0 : (byte)255;
            }
        }

        return pixels;
    }
}
