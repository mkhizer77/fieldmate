using System;
using System.Linq;
using Fieldmate.Twin;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Twin;

/// <summary>#88: the pressure dial's scale and colour bands.</summary>
public class GaugeScaleTests
{
    [Test]
    public void Pressure_dial_sweeps_270_degrees_centred_on_twelve()
    {
        var scale = GaugeScale.Pressure;
        Assert.That(scale.Angle(0f), Is.EqualTo(-135f).Within(1e-4f), "0 bar at the lower left");
        Assert.That(scale.Angle(5f), Is.EqualTo(0f).Within(1e-4f), "mid-scale straight up");
        Assert.That(scale.Angle(10f), Is.EqualTo(135f).Within(1e-4f), "10 bar at the lower right");
        Assert.That(scale.Angle(4f), Is.EqualTo(-27f).Within(1e-4f), "nominal running pressure just left of top");
    }

    [Test]
    public void Needle_stops_at_the_pins_past_either_end()
    {
        var scale = GaugeScale.Pressure;
        Assert.That(scale.Angle(-3f), Is.EqualTo(-135f - GaugeScale.StopDegrees).Within(1e-4f));
        Assert.That(scale.Angle(42f), Is.EqualTo(135f + GaugeScale.StopDegrees).Within(1e-4f));
        Assert.Throws<ArgumentException>(() => _ = new GaugeScale(5f, 5f, 270f));
    }

    [Test]
    public void Bands_follow_the_twin_and_the_fault_reads_in_the_red()
    {
        var bands = GaugeBand.Pressure();
        var spec = TelemetryModel.Spec(TelemetryChannel.Pressure);
        Assert.That(bands.Select(b => b.Status), Is.EqualTo(new[] { ChannelStatus.Normal, ChannelStatus.Warning, ChannelStatus.Alarm }));
        Assert.That(bands[0].From, Is.EqualTo(3f), "green covers what 'verify running pressure' (4 ± 1 bar) accepts");
        Assert.That(bands[0].To, Is.EqualTo(spec.Warning));
        Assert.That(bands[1].To, Is.EqualTo(spec.Alarm));
        Assert.That(bands[2].To, Is.EqualTo(GaugeScale.Pressure.Max));

        var faulty = new TelemetryModel(FaultModel.CreateDefault());
        faulty.Faults.Inject(FaultModel.Overpressure);
        faulty.Step(60f);
        var reading = faulty[TelemetryChannel.Pressure];
        Assert.That(reading, Is.GreaterThan(bands[2].From).And.LessThan(GaugeScale.Pressure.Max), "the stuck relief valve reads in the red, on scale");
    }
}
