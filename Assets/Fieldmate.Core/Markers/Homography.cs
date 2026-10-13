using System;
using UnityEngine;

namespace Fieldmate.Markers;

/// <summary>A 3×3 planar projective map, solved from four point pairs (direct linear transform).</summary>
public readonly struct Homography
{
    private readonly double[] m; // row-major, m[8] = 1

    private Homography(double[] values) => m = values;

    public double this[int row, int column] => m[row * 3 + column];

    /// <summary>The map taking (0,0), (1,0), (1,1), (0,1) to <paramref name="to"/>[0..3].</summary>
    public static Homography FromUnitSquare(Vector2[] to) =>
        Solve(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, to);

    /// <summary>The map taking each <paramref name="from"/> point to the matching <paramref name="to"/> point.</summary>
    public static Homography Solve(Vector2[] from, Vector2[] to)
    {
        if (from == null || to == null || from.Length != 4 || to.Length != 4)
        {
            throw new ArgumentException("A homography needs exactly four point pairs.");
        }

        // Unknowns h0..h7 (h8 = 1): u = (h0 x + h1 y + h2) / (h6 x + h7 y + 1), v likewise with h3..h5.
        var a = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            double x = from[i].x, y = from[i].y, u = to[i].x, v = to[i].y;
            var r = 2 * i;
            a[r, 0] = x; a[r, 1] = y; a[r, 2] = 1; a[r, 6] = -u * x; a[r, 7] = -u * y; a[r, 8] = u;
            a[r + 1, 3] = x; a[r + 1, 4] = y; a[r + 1, 5] = 1; a[r + 1, 6] = -v * x; a[r + 1, 7] = -v * y; a[r + 1, 8] = v;
        }

        for (var col = 0; col < 8; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < 8; row++)
            {
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(a[pivot, col]) < 1e-12)
            {
                throw new ArgumentException("Degenerate points: three of them are on a line.");
            }

            for (var k = 0; k < 9; k++)
            {
                (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
            }

            for (var row = 0; row < 8; row++)
            {
                if (row == col)
                {
                    continue;
                }

                var f = a[row, col] / a[col, col];
                for (var k = col; k < 9; k++)
                {
                    a[row, k] -= f * a[col, k];
                }
            }
        }

        var h = new double[9];
        for (var i = 0; i < 8; i++)
        {
            h[i] = a[i, 8] / a[i, i];
        }

        h[8] = 1;
        return new Homography(h);
    }

    public Vector2 Map(Vector2 p)
    {
        var w = m[6] * p.x + m[7] * p.y + m[8];
        return new Vector2((float)((m[0] * p.x + m[1] * p.y + m[2]) / w), (float)((m[3] * p.x + m[4] * p.y + m[5]) / w));
    }
}
