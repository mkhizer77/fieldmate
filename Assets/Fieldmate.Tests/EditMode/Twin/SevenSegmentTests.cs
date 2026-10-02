using Fieldmate.Twin;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Twin;

/// <summary>#88: the gauge's digital window, three seven-segment digits with a fixed decimal point.</summary>
public class SevenSegmentTests
{
    private readonly byte[] masks = new byte[SevenSegment.Digits];

    [Test]
    public void Digits_light_the_usual_segments()
    {
        Assert.That(SevenSegment.Mask(8), Is.EqualTo(0x7F), "all seven");
        Assert.That(SevenSegment.Mask(1), Is.EqualTo(0x06), "b and c");
        Assert.That(SevenSegment.IsLit(SevenSegment.Mask(0), 6), Is.False, "zero has no middle bar");
        Assert.That(SevenSegment.IsLit(SevenSegment.Mask(6), 0), Is.True, "six has a top bar");
    }

    [Test]
    public void Fault_reading_shows_six_point_eight_with_the_tens_dark()
    {
        Assert.That(SevenSegment.Tenths(6.84f, masks), Is.EqualTo(68));
        Assert.That(masks, Is.EqualTo(new[] { (byte)0, SevenSegment.Mask(6), SevenSegment.Mask(8) }));
    }

    [Test]
    public void Zero_ten_and_out_of_range_values()
    {
        SevenSegment.Tenths(0.04f, masks);
        Assert.That(masks, Is.EqualTo(new[] { (byte)0, SevenSegment.Mask(0), SevenSegment.Mask(0) }), "0.0, not blank");
        SevenSegment.Tenths(-1f, masks);
        Assert.That(masks[1], Is.EqualTo(SevenSegment.Mask(0)), "never negative");
        SevenSegment.Tenths(10f, masks);
        Assert.That(masks, Is.EqualTo(new[] { SevenSegment.Mask(1), SevenSegment.Mask(0), SevenSegment.Mask(0) }));
        Assert.That(SevenSegment.Tenths(500f, masks), Is.EqualTo(999));
        Assert.That(SevenSegment.Tenths(4.05f, masks), Is.EqualTo(41).Or.EqualTo(40), "midpoint, either is a fair reading");
    }
}
