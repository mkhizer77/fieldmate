using System;
using Fieldmate.Twin;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Twin;

/// <summary>#88: the printed pressure dial has its bands, ticks, numerals and readout window where the needle expects.</summary>
public class DialPainterTests
{
    private const int Size = 256;
    private static byte[] dial;

    [OneTimeSetUp]
    public void Paint() =>
        dial = DialPainter.Paint(Size, GaugeScale.Pressure, GaugeBand.Pressure(), majorStep: 1f, minorStep: 0.5f, numeralStep: 2f, unit: "bar");

    // The colour at a point on the dial (centre 0, radius 1, x right, y up as seen from the front).
    private static (float r, float g, float b) At(DialPainter.Vec p)
    {
        var x = (int)((p.X + 1f) * 0.5f * Size);
        var y = (int)((p.Y + 1f) * 0.5f * Size);
        var i = (y * Size + x) * 4;
        return (dial[i] / 255f, dial[i + 1] / 255f, dial[i + 2] / 255f);
    }

    private static void AssertColour((float r, float g, float b) actual, DialPainter.Rgb expected, string what)
    {
        Assert.That(Math.Abs(actual.r - expected.R) + Math.Abs(actual.g - expected.G) + Math.Abs(actual.b - expected.B),
            Is.LessThan(0.06f), $"{what}: got ({actual.r:0.00}, {actual.g:0.00}, {actual.b:0.00})");
    }

    private static DialPainter.Vec OnBand(float bar) =>
        DialPainter.Polar((DialPainter.BandInner + DialPainter.BandOuter) * 0.5f, GaugeScale.Pressure.Angle(bar));

    [Test]
    public void Image_is_square_rgba_and_opaque()
    {
        Assert.That(dial.Length, Is.EqualTo(Size * Size * 4));
        for (var i = 3; i < dial.Length; i += 4)
        {
            Assert.That(dial[i], Is.EqualTo(255));
        }
    }

    [Test]
    public void Bands_are_green_at_running_pressure_amber_at_warning_red_at_the_fault()
    {
        AssertColour(At(OnBand(3.75f)), DialPainter.Green, "3.75 bar");
        AssertColour(At(OnBand(5.25f)), DialPainter.Amber, "5.25 bar");
        AssertColour(At(OnBand(6.75f)), DialPainter.Red, "6.75 bar, where the stuck relief valve leaves it");
        AssertColour(At(OnBand(1.25f)), DialPainter.Face, "below the green band the face is bare");
    }

    [Test]
    public void Major_ticks_are_printed_at_every_bar_minor_at_half_bars()
    {
        AssertColour(At(DialPainter.Polar(0.74f, GaugeScale.Pressure.Angle(1f))), DialPainter.Ink, "major tick at 1 bar reaches into the band ring");
        AssertColour(At(DialPainter.Polar(0.84f, GaugeScale.Pressure.Angle(1.5f))), DialPainter.Ink, "minor tick at 1.5 bar");
        AssertColour(At(DialPainter.Polar(0.74f, GaugeScale.Pressure.Angle(1.5f))), DialPainter.Face, "minor ticks stay short");
    }

    [Test]
    public void Numerals_sit_inside_the_ticks_and_the_window_under_the_hub()
    {
        foreach (var bar in new[] { 0f, 4f, 10f })
        {
            var centre = DialPainter.Polar(DialPainter.NumeralRadius, GaugeScale.Pressure.Angle(bar));
            var inked = 0;
            for (var dy = -0.07f; dy <= 0.07f; dy += 0.01f)
            {
                for (var dx = -0.09f; dx <= 0.09f; dx += 0.01f)
                {
                    var c = At(centre + new DialPainter.Vec(dx, dy));
                    inked += c.r < 0.4f ? 1 : 0;
                }
            }

            Assert.That(inked, Is.GreaterThan(8), $"the numeral for {bar} bar is printed");
        }

        AssertColour(At(new DialPainter.Vec(0f, DialPainter.WindowY)), DialPainter.Window, "readout window");
        AssertColour(At(new DialPainter.Vec(0.95f, 0.95f)), DialPainter.Rim, "outside the dial");
        AssertColour(At(new DialPainter.Vec(0.2f, 0.12f)), DialPainter.Face, "open face near the hub");
    }
}
