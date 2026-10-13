using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fieldmate.Markers;

/// <summary>A machine tag found in an image: its id and its four outer corners in pixels (top-left origin, y down).</summary>
public readonly struct MarkerDetection
{
    public MarkerDetection(int id, Vector2[] corners, int errors)
    {
        Id = id;
        Corners = corners;
        Errors = errors;
    }

    public int Id { get; }

    /// <summary>The tag's own top-left, top-right, bottom-right and bottom-left corners, whatever its rotation in the image.</summary>
    public Vector2[] Corners { get; }

    /// <summary>Data cells that read wrong (≤ <see cref="MarkerDictionary.MaxCorrectable"/>).</summary>
    public int Errors { get; }

    public Vector2 Center => (Corners[0] + Corners[1] + Corners[2] + Corners[3]) * 0.25f;

    /// <summary>Where the tag's real centre appears: the diagonals' crossing (the corner average drifts under perspective).</summary>
    public Vector2 ProjectiveCenter
    {
        get
        {
            Vector2 p = Corners[0], r = Corners[2] - Corners[0], q = Corners[1], s = Corners[3] - Corners[1];
            var denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 1e-6f)
            {
                return Center;
            }

            var t = ((q.x - p.x) * s.y - (q.y - p.y) * s.x) / denom;
            return p + r * t;
        }
    }
}

/// <summary>
/// Finds Fieldmate machine tags in a grayscale image (#27), on the headset, from one camera frame: adaptive threshold,
/// dark connected regions, a quadrilateral from each region's convex outline, a perspective read of the 7×7 cells, and
/// a dictionary match that corrects up to two wrong cells. No external libraries; run on demand, never per frame.
/// </summary>
public static class MarkerDetector
{
    /// <summary>Smallest tag side in pixels worth reading (about 3 px per cell).</summary>
    public const int MinSidePixels = 21;

    /// <param name="gray">Luminance, row-major, row 0 at the top.</param>
    public static List<MarkerDetection> Detect(byte[] gray, int width, int height)
    {
        if (gray == null || gray.Length < width * height || width < MinSidePixels || height < MinSidePixels)
        {
            throw new ArgumentException("The image is smaller than its size says, or too small for a tag.");
        }

        var dark = Threshold(gray, width, height);
        var labels = new int[width * height];
        var found = new Dictionary<int, MarkerDetection>();
        var stack = new Stack<int>();
        var hullInput = new List<Vector2>();
        var label = 0;
        for (var start = 0; start < labels.Length; start++)
        {
            if (!dark[start] || labels[start] != 0)
            {
                continue;
            }

            label++;
            hullInput.Clear();
            int minX = width, minY = height, maxX = 0, maxY = 0, area = 0;
            labels[start] = label;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                var x = p % width;
                var y = p / width;
                area++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                var edge = false;
                edge |= Visit(x - 1, y, width, height, dark, labels, label, stack);
                edge |= Visit(x + 1, y, width, height, dark, labels, label, stack);
                edge |= Visit(x, y - 1, width, height, dark, labels, label, stack);
                edge |= Visit(x, y + 1, width, height, dark, labels, label, stack);
                if (edge)
                {
                    hullInput.Add(new Vector2(x + 0.5f, y + 0.5f));
                }
            }

            var spanX = maxX - minX + 1;
            var spanY = maxY - minY + 1;
            if (spanX < MinSidePixels && spanY < MinSidePixels || minX == 0 || minY == 0 || maxX == width - 1 || maxY == height - 1)
            {
                continue; // too small, or cut off by the image edge
            }

            // The border ring alone covers 24 of 49 cells; with data at least ~40 % of the box is dark.
            if (area < 0.25f * spanX * spanY)
            {
                continue;
            }

            if (!TryQuad(hullInput, out var quad))
            {
                continue;
            }

            Refine(quad, hullInput);
            if (!TryRead(gray, width, height, quad, out var detection))
            {
                continue;
            }

            if (!found.TryGetValue(detection.Id, out var existing) || detection.Errors < existing.Errors)
            {
                found[detection.Id] = detection;
            }
        }

        return new List<MarkerDetection>(found.Values);
    }

    /// <summary>RGBA32 rows bottom-up (a Unity texture) → luminance rows top-down.</summary>
    public static byte[] Luminance(byte[] rgba, int width, int height, byte[] into = null)
    {
        var gray = into != null && into.Length >= width * height ? into : new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var source = (height - 1 - y) * width * 4;
            var target = y * width;
            for (var x = 0; x < width; x++, source += 4)
            {
                gray[target + x] = (byte)((rgba[source] * 77 + rgba[source + 1] * 150 + rgba[source + 2] * 29) >> 8);
            }
        }

        return gray;
    }

    private static bool Visit(int x, int y, int width, int height, bool[] dark, int[] labels, int label, Stack<int> stack)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return true;
        }

        var p = y * width + x;
        if (!dark[p])
        {
            return true; // a light neighbour: this pixel is on the region's outline
        }

        if (labels[p] == 0)
        {
            labels[p] = label;
            stack.Push(p);
        }

        return false;
    }

    // Dark = clearly darker than the local mean (integral image, window ~1/16 of the image).
    private static bool[] Threshold(byte[] gray, int width, int height)
    {
        var integral = new long[(width + 1) * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            long row = 0;
            for (var x = 0; x < width; x++)
            {
                row += gray[y * width + x];
                integral[(y + 1) * (width + 1) + x + 1] = integral[y * (width + 1) + x + 1] + row;
            }
        }

        var half = Math.Max(7, Math.Max(width, height) / 16);
        var dark = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            var y0 = Math.Max(0, y - half);
            var y1 = Math.Min(height, y + half + 1);
            for (var x = 0; x < width; x++)
            {
                var x0 = Math.Max(0, x - half);
                var x1 = Math.Min(width, x + half + 1);
                var sum = integral[y1 * (width + 1) + x1] - integral[y0 * (width + 1) + x1] - integral[y1 * (width + 1) + x0] + integral[y0 * (width + 1) + x0];
                var mean = sum / (double)((x1 - x0) * (y1 - y0));
                dark[y * width + x] = gray[y * width + x] < mean - 10;
            }
        }

        return dark;
    }

    // Convex hull, then the four hull points that best span it: a quadrilateral if the region is square-ish.
    private static bool TryQuad(List<Vector2> points, out Vector2[] quad)
    {
        quad = null;
        var hull = ConvexHull(points);
        if (hull.Count < 4)
        {
            return false;
        }

        // Diagonal 1: the farthest pair.
        int a = 0, b = 0;
        var best = -1f;
        for (var i = 0; i < hull.Count; i++)
        {
            for (var j = i + 1; j < hull.Count; j++)
            {
                var d = (hull[i] - hull[j]).sqrMagnitude;
                if (d > best)
                {
                    best = d;
                    a = i;
                    b = j;
                }
            }
        }

        // The other two corners: farthest from that diagonal on each side.
        int c = -1, d2 = -1;
        float left = 0f, right = 0f;
        for (var i = 0; i < hull.Count; i++)
        {
            var side = Cross(hull[b] - hull[a], hull[i] - hull[a]);
            if (side > left)
            {
                left = side;
                c = i;
            }
            else if (side < right)
            {
                right = side;
                d2 = i;
            }
        }

        if (c < 0 || d2 < 0)
        {
            return false;
        }

        // Order around the outline: a, then the corner on one side, b, the corner on the other side.
        quad = new[] { hull[a], hull[d2], hull[b], hull[c] };
        if (Cross(quad[1] - quad[0], quad[2] - quad[1]) < 0f)
        {
            quad = new[] { hull[a], hull[c], hull[b], hull[d2] };
        }

        // Square-ish: opposite sides within 3× of each other and the hull area mostly covered by the quad.
        var s0 = (quad[1] - quad[0]).magnitude;
        var s1 = (quad[2] - quad[1]).magnitude;
        var s2 = (quad[3] - quad[2]).magnitude;
        var s3 = (quad[0] - quad[3]).magnitude;
        if (Mathf.Min(s0, s1, s2, s3) < MinSidePixels * 0.7f || Mathf.Max(s0, s2) > 3f * Mathf.Min(s0, s2) || Mathf.Max(s1, s3) > 3f * Mathf.Min(s1, s3))
        {
            return false;
        }

        return Area(quad) > 0.85f * Area(hull);
    }

    // Reads the 7×7 cells through the quad's perspective; the border must be dark and the margin light.
    private static bool TryRead(byte[] gray, int width, int height, Vector2[] quad, out MarkerDetection detection)
    {
        detection = default;
        var h = Homography.FromUnitSquare(quad); // (0,0)→quad[0] … (1,1)→quad[2]
        const int n = MarkerDictionary.GridCells;
        float Sample(float cx, float cy)
        {
            var p = h.Map(new Vector2(cx / n, cy / n));
            var x = Mathf.Clamp(p.x - 0.5f, 0f, width - 1.001f);
            var y = Mathf.Clamp(p.y - 0.5f, 0f, height - 1.001f);
            var x0 = (int)x;
            var y0 = (int)y;
            var fx = x - x0;
            var fy = y - y0;
            var i = y0 * width + x0;
            return Mathf.Lerp(Mathf.Lerp(gray[i], gray[i + 1], fx), Mathf.Lerp(gray[i + width], gray[i + width + 1], fx), fy);
        }

        // Cell centres sit at (k + 0.5); the margin ring just outside the border at -0.35 and n + 0.35.
        var values = new float[n, n];
        float borderSum = 0f;
        var borderCount = 0;
        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < n; column++)
            {
                values[row, column] = Sample(column + 0.5f, row + 0.5f);
                if (row == 0 || column == 0 || row == n - 1 || column == n - 1)
                {
                    borderSum += values[row, column];
                    borderCount++;
                }
            }
        }

        float marginSum = 0f;
        var marginCount = 0;
        for (var k = 0; k < n; k++)
        {
            marginSum += Sample(k + 0.5f, -0.35f) + Sample(k + 0.5f, n + 0.35f) + Sample(-0.35f, k + 0.5f) + Sample(n + 0.35f, k + 0.5f);
            marginCount += 4;
        }

        var darkLevel = borderSum / borderCount;
        var lightLevel = marginSum / marginCount;
        if (lightLevel - darkLevel < 30f)
        {
            return false; // no contrast between border and margin: not a tag
        }

        var cut = (darkLevel + lightLevel) * 0.5f;
        var badBorder = 0;
        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < n; column++)
            {
                if ((row == 0 || column == 0 || row == n - 1 || column == n - 1) && values[row, column] > cut)
                {
                    badBorder++;
                }
            }
        }

        if (badBorder > 2)
        {
            return false;
        }

        uint code = 0;
        for (var row = 0; row < MarkerDictionary.DataCells; row++)
        {
            for (var column = 0; column < MarkerDictionary.DataCells; column++)
            {
                if (values[row + 1, column + 1] < cut)
                {
                    code |= MarkerDictionary.Mask(row, column);
                }
            }
        }

        if (!MarkerDictionary.TryMatch(code, out var id, out var rotation, out var errors))
        {
            return false;
        }

        // The read grid is the tag turned `rotation` quarter turns clockwise, so the tag's own top-left sits at the read
        // grid's corner `rotation` (top-left, top-right, bottom-right, bottom-left).
        var corners = new Vector2[4];
        for (var k = 0; k < 4; k++)
        {
            corners[k] = quad[(k + rotation) % 4];
        }

        detection = new MarkerDetection(id, corners, errors);
        return true;
    }

    // Sub-pixel corners: fit a line to the outline pixels along each side, move it out half a pixel (outline pixel
    // centres sit inside the true edge), and intersect neighbouring sides. Keeps the hull corner when a fit is unsure.
    private static void Refine(Vector2[] quad, List<Vector2> outline)
    {
        var lines = new (Vector2 point, Vector2 direction)[4];
        var centre = (quad[0] + quad[1] + quad[2] + quad[3]) * 0.25f;
        for (var side = 0; side < 4; side++)
        {
            var a = quad[side];
            var b = quad[(side + 1) % 4];
            var along = b - a;
            var length = along.magnitude;
            if (length < 1f)
            {
                return;
            }

            along /= length;
            var normal = new Vector2(-along.y, along.x);
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            var count = 0;
            foreach (var p in outline)
            {
                var t = Vector2.Dot(p - a, along);
                if (t < length * 0.15f || t > length * 0.85f || Mathf.Abs(Vector2.Dot(p - a, normal)) > 2f)
                {
                    continue; // near the corners the outline bends; away from the side it's another edge
                }

                sx += p.x;
                sy += p.y;
                sxx += p.x * p.x;
                syy += p.y * p.y;
                sxy += p.x * p.y;
                count++;
            }

            if (count < 4)
            {
                return;
            }

            // Principal direction of the points (total least squares).
            var mx = sx / count;
            var my = sy / count;
            var cxx = sxx / count - mx * mx;
            var cyy = syy / count - my * my;
            var cxy = sxy / count - mx * my;
            var angle = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
            var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            var outward = new Vector2(-direction.y, direction.x);
            var mid = new Vector2((float)mx, (float)my);
            if (Vector2.Dot(mid - centre, outward) < 0f)
            {
                outward = -outward;
            }

            lines[side] = (mid + outward * 0.5f, direction);
        }

        var refined = new Vector2[4];
        for (var k = 0; k < 4; k++)
        {
            // Corner k joins side k-1 (ending at it) and side k (starting at it).
            var (p1, d1) = lines[(k + 3) % 4];
            var (p2, d2) = lines[k];
            var denom = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(denom) < 1e-3f)
            {
                return;
            }

            var t = ((p2.x - p1.x) * d2.y - (p2.y - p1.y) * d2.x) / denom;
            refined[k] = p1 + d1 * t;
            if ((refined[k] - quad[k]).sqrMagnitude > 9f)
            {
                return; // more than 3 px from the hull corner: trust the hull
            }
        }

        Array.Copy(refined, quad, 4);
    }

    private static List<Vector2> ConvexHull(List<Vector2> points)
    {
        var sorted = new List<Vector2>(points);
        sorted.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
        var hull = new List<Vector2>(2 * sorted.Count);
        for (var pass = 0; pass < 2; pass++)
        {
            var start = hull.Count;
            for (var k = 0; k < sorted.Count; k++)
            {
                var p = pass == 0 ? sorted[k] : sorted[sorted.Count - 1 - k];
                while (hull.Count >= start + 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 2]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static float Area(IReadOnlyList<Vector2> polygon)
    {
        var sum = 0f;
        for (var i = 0; i < polygon.Count; i++)
        {
            sum += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        }

        return Mathf.Abs(sum) * 0.5f;
    }
}
