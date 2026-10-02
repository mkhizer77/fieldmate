using System;
using System.Collections.Generic;
using System.Globalization;

namespace Fieldmate.Twin;

/// <summary>
/// Paints a printed gauge dial (#88) into an RGBA32 image, rows bottom-up (Unity's texture order), as seen from the
/// front: the face, the colour bands, major and minor ticks, numerals in a small stroke font, the unit and the dark
/// window the digital readout sits in. Anti-aliased by distance; pure C#, so it is unit-tested and baked by the scene
/// builder rather than drawn at runtime. Radii are fractions of the dial radius (the image's half-width).
/// </summary>
public static class DialPainter
{
    public static readonly Rgb Face = new(0.96f, 0.96f, 0.94f);
    public static readonly Rgb Ink = new(0.09f, 0.09f, 0.1f);
    public static readonly Rgb Rim = new(0.16f, 0.17f, 0.18f);
    public static readonly Rgb Window = new(0.07f, 0.08f, 0.09f);
    public static readonly Rgb Green = new(0.16f, 0.62f, 0.28f);
    public static readonly Rgb Amber = new(0.96f, 0.66f, 0.1f);
    public static readonly Rgb Red = new(0.84f, 0.15f, 0.12f);

    public const float BandInner = 0.70f;
    public const float BandOuter = 0.80f;
    public const float TickOuter = 0.88f;
    public const float MajorInner = 0.70f;
    public const float MinorInner = 0.79f;
    public const float NumeralRadius = 0.55f;
    public const float NumeralHeight = 0.15f;

    /// <summary>Centre of the readout window, as a fraction of the radius below the hub.</summary>
    public const float WindowY = -0.64f;
    public const float WindowWidth = 0.5f;
    public const float WindowHeight = 0.2f;

    /// <summary>The image, <paramref name="size"/>² RGBA32, bottom row first.</summary>
    public static byte[] Paint(int size, GaugeScale scale, IReadOnlyList<GaugeBand> bands, float majorStep, float minorStep,
        float numeralStep, string unit)
    {
        if (size < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var canvas = new Canvas(size);
        canvas.FillDisc(1f, Face, Rim);

        foreach (var band in bands)
        {
            canvas.FillArc(BandInner, BandOuter, scale.Angle(band.From), scale.Angle(band.To), BandColour(band.Status));
        }

        for (var v = scale.Min; v <= scale.Max + 1e-4f; v += minorStep)
        {
            var major = IsMultiple(v - scale.Min, majorStep);
            canvas.Stroke(Polar(major ? MajorInner : MinorInner, scale.Angle(v)), Polar(TickOuter, scale.Angle(v)),
                major ? 0.022f : 0.01f, Ink);
        }

        for (var v = scale.Min; v <= scale.Max + 1e-4f; v += numeralStep)
        {
            var label = Math.Round(v).ToString(CultureInfo.InvariantCulture);
            canvas.Text(label, Polar(NumeralRadius, scale.Angle(v)), NumeralHeight, Ink);
        }

        if (!string.IsNullOrEmpty(unit))
        {
            canvas.Text(unit, new Vec(0f, 0.33f), 0.1f, Ink);
        }

        canvas.FillRoundedRect(new Vec(0f, WindowY), WindowWidth * 0.5f, WindowHeight * 0.5f, 0.05f, Window);
        return canvas.Pixels;
    }

    public static Rgb BandColour(ChannelStatus status) =>
        status == ChannelStatus.Alarm ? Red : status == ChannelStatus.Warning ? Amber : Green;

    /// <summary>A point on the dial at <paramref name="radius"/>, <paramref name="degrees"/> clockwise from twelve.</summary>
    public static Vec Polar(float radius, float degrees)
    {
        var a = degrees * Math.PI / 180.0;
        return new Vec((float)(radius * Math.Sin(a)), (float)(radius * Math.Cos(a)));
    }

    private static bool IsMultiple(float value, float step)
    {
        var q = value / step;
        return Math.Abs(q - Math.Round(q)) < 1e-3;
    }

    public readonly struct Rgb
    {
        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public float R { get; }
        public float G { get; }
        public float B { get; }
    }

    public readonly struct Vec
    {
        public Vec(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }

        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
        public static Vec operator *(Vec a, float s) => new(a.X * s, a.Y * s);
        public float Dot(Vec b) => X * b.X + Y * b.Y;
        public float Length => (float)Math.Sqrt(X * X + Y * Y);
    }

    /// <summary>Dial coordinates: centre (0, 0), radius 1, x right and y up as seen from the front.</summary>
    private sealed class Canvas
    {
        private readonly int size;
        private readonly float pixel; // one pixel in dial units

        public Canvas(int size)
        {
            this.size = size;
            pixel = 2f / size;
            Pixels = new byte[size * size * 4];
        }

        public byte[] Pixels { get; }

        private Vec At(int x, int y) => new((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);

        private void Blend(int x, int y, Rgb c, float coverage)
        {
            if (coverage <= 0f)
            {
                return;
            }

            coverage = Math.Min(1f, coverage);
            var i = (y * size + x) * 4;
            Pixels[i] = Mix(Pixels[i], c.R, coverage);
            Pixels[i + 1] = Mix(Pixels[i + 1], c.G, coverage);
            Pixels[i + 2] = Mix(Pixels[i + 2], c.B, coverage);
            Pixels[i + 3] = 255;
        }

        private static byte Mix(byte under, float over, float t) => (byte)Math.Round(under + (over * 255f - under) * t);

        // Coverage of a shape at signed distance d (dial units; negative inside), over about one pixel.
        private float Cover(float d) => Math.Max(0f, Math.Min(1f, 0.5f - d / pixel));

        public void FillDisc(float radius, Rgb inside, Rgb outside)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var p = At(x, y);
                    Blend(x, y, outside, 1f);
                    Blend(x, y, inside, Cover(p.Length - radius));
                }
            }
        }

        public void FillArc(float inner, float outer, float fromDegrees, float toDegrees, Rgb colour)
        {
            var mid = (fromDegrees + toDegrees) * 0.5f;
            var half = Math.Abs(toDegrees - fromDegrees) * 0.5f;
            ForBox(new Vec(-outer, -outer), new Vec(outer, outer), (x, y, p) =>
            {
                var r = p.Length;
                var radial = Math.Max(inner - r, r - outer);
                var angle = (float)(Math.Atan2(p.X, p.Y) * 180.0 / Math.PI); // clockwise from twelve
                var angular = (Math.Abs(DeltaDegrees(angle, mid)) - half) * (float)(Math.PI / 180.0) * r;
                Blend(x, y, colour, Cover(Math.Max(radial, angular)));
            });
        }

        public void Stroke(Vec a, Vec b, float width, Rgb colour)
        {
            var half = width * 0.5f;
            var min = new Vec(Math.Min(a.X, b.X) - half, Math.Min(a.Y, b.Y) - half);
            var max = new Vec(Math.Max(a.X, b.X) + half, Math.Max(a.Y, b.Y) + half);
            var ab = b - a;
            var lengthSq = Math.Max(1e-12f, ab.Dot(ab));
            ForBox(min, max, (x, y, p) =>
            {
                var t = Math.Max(0f, Math.Min(1f, (p - a).Dot(ab) / lengthSq));
                Blend(x, y, colour, Cover((p - (a + ab * t)).Length - half));
            });
        }

        public void FillRoundedRect(Vec centre, float halfWidth, float halfHeight, float corner, Rgb colour)
        {
            ForBox(new Vec(centre.X - halfWidth, centre.Y - halfHeight), new Vec(centre.X + halfWidth, centre.Y + halfHeight), (x, y, p) =>
            {
                var qx = Math.Abs(p.X - centre.X) - (halfWidth - corner);
                var qy = Math.Abs(p.Y - centre.Y) - (halfHeight - corner);
                var outside = new Vec(Math.Max(qx, 0f), Math.Max(qy, 0f)).Length + Math.Min(Math.Max(qx, qy), 0f) - corner;
                Blend(x, y, colour, Cover(outside));
            });
        }

        /// <summary>Text centred on <paramref name="centre"/>, <paramref name="height"/> tall (digits and capitals).</summary>
        public void Text(string text, Vec centre, float height, Rgb colour)
        {
            var advance = StrokeFont.Advance * height;
            var width = advance * text.Length - (advance - StrokeFont.Width * height);
            var origin = new Vec(centre.X - width * 0.5f, centre.Y - height * 0.5f);
            var stroke = StrokeFont.Weight * height;
            for (var i = 0; i < text.Length; i++)
            {
                foreach (var line in StrokeFont.Glyph(text[i]))
                {
                    for (var j = 1; j < line.Length; j++)
                    {
                        Stroke(origin + line[j - 1] * height, origin + line[j] * height, stroke, colour);
                    }
                }

                origin += new Vec(advance, 0f);
            }
        }

        private void ForBox(Vec min, Vec max, Action<int, int, Vec> paint)
        {
            var x0 = Math.Max(0, (int)Math.Floor((min.X + 1f) / pixel) - 1);
            var x1 = Math.Min(size - 1, (int)Math.Ceiling((max.X + 1f) / pixel) + 1);
            var y0 = Math.Max(0, (int)Math.Floor((min.Y + 1f) / pixel) - 1);
            var y1 = Math.Min(size - 1, (int)Math.Ceiling((max.Y + 1f) / pixel) + 1);
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    paint(x, y, At(x, y));
                }
            }
        }

        private static float DeltaDegrees(float a, float b)
        {
            var d = (a - b) % 360f;
            return d > 180f ? d - 360f : d < -180f ? d + 360f : d;
        }
    }

    /// <summary>
    /// A small engineering stroke font for the dial: digits and the lower-case letters of units. Glyphs are polylines in a
    /// box 0.6 wide and 1 tall (x right, y up); arcs are sampled.
    /// </summary>
    private static class StrokeFont
    {
        public const float Width = 0.6f;
        public const float Advance = 0.82f;
        public const float Weight = 0.13f;

        private static readonly Dictionary<char, Vec[][]> Glyphs = Build();

        public static Vec[][] Glyph(char c) => Glyphs.TryGetValue(c, out var glyph) ? glyph : Array.Empty<Vec[]>();

        private static Dictionary<char, Vec[][]> Build() => new()
        {
            ['0'] = new[] { Ellipse(0.3f, 0.5f, 0.3f, 0.5f, 0f, 360f) },
            ['1'] = new[] { new[] { new Vec(0.1f, 0.78f), new Vec(0.34f, 1f), new Vec(0.34f, 0f) } },
            ['2'] = new[] { Join(Ellipse(0.3f, 0.7f, 0.3f, 0.3f, -70f, 120f), new Vec(0f, 0f), new Vec(0.6f, 0f)) },
            ['3'] = new[] { Ellipse(0.3f, 0.75f, 0.27f, 0.25f, -60f, 180f), Ellipse(0.3f, 0.27f, 0.3f, 0.27f, 0f, 240f) },
            ['4'] = new[] { new[] { new Vec(0.46f, 0f), new Vec(0.46f, 1f), new Vec(0f, 0.32f), new Vec(0.6f, 0.32f) } },
            ['5'] = new[] { Join(new[] { new Vec(0.56f, 1f), new Vec(0.1f, 1f), new Vec(0.06f, 0.56f) }, Ellipse(0.3f, 0.32f, 0.3f, 0.32f, -50f, 210f)) },
            ['6'] = new[] { Ellipse(0.3f, 0.3f, 0.3f, 0.3f, 0f, 360f), new[] { new Vec(0.01f, 0.34f), new Vec(0.16f, 0.76f), new Vec(0.46f, 1f) } },
            ['7'] = new[] { new[] { new Vec(0f, 1f), new Vec(0.6f, 1f), new Vec(0.2f, 0f) } },
            ['8'] = new[] { Ellipse(0.3f, 0.76f, 0.25f, 0.24f, 0f, 360f), Ellipse(0.3f, 0.27f, 0.3f, 0.27f, 0f, 360f) },
            ['9'] = new[] { Ellipse(0.3f, 0.7f, 0.3f, 0.3f, 0f, 360f), new[] { new Vec(0.59f, 0.66f), new Vec(0.44f, 0.24f), new Vec(0.14f, 0f) } },
            ['b'] = new[] { new[] { new Vec(0.02f, 1f), new Vec(0.02f, 0f) }, Ellipse(0.31f, 0.29f, 0.29f, 0.29f, 0f, 360f) },
            ['a'] = new[] { Ellipse(0.29f, 0.29f, 0.29f, 0.29f, 0f, 360f), new[] { new Vec(0.58f, 0.58f), new Vec(0.58f, 0f) } },
            ['r'] = new[] { new[] { new Vec(0.04f, 0.58f), new Vec(0.04f, 0f) }, Ellipse(0.44f, 0.18f, 0.4f, 0.4f, -90f, -10f) },
        };

        // Points on an ellipse from one angle to another, degrees measured clockwise from twelve o'clock.
        private static Vec[] Ellipse(float cx, float cy, float rx, float ry, float from, float to)
        {
            var steps = Math.Max(4, (int)(Math.Abs(to - from) / 10f));
            var points = new Vec[steps + 1];
            for (var i = 0; i <= steps; i++)
            {
                var a = (from + (to - from) * i / steps) * Math.PI / 180.0;
                points[i] = new Vec(cx + rx * (float)Math.Sin(a), cy + ry * (float)Math.Cos(a));
            }

            return points;
        }

        private static Vec[] Join(Vec[] first, params Vec[] rest)
        {
            var all = new Vec[first.Length + rest.Length];
            first.CopyTo(all, 0);
            rest.CopyTo(all, first.Length);
            return all;
        }
    }
}
