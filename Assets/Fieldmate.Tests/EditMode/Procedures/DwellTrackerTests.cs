using Fieldmate.Procedures;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Procedures;

/// <summary>Device test 2026-09-30: the gaze slipping off a small gauge for a few frames must not reset the dwell.</summary>
public class DwellTrackerTests
{
    [Test]
    public void Short_slip_keeps_the_dwell()
    {
        var dwell = new DwellTracker();
        for (var i = 0; i < 11; i++) dwell.Update("pressure_gauge", 0.1f); // the first frame starts the clock
        Assert.That(dwell.Seconds, Is.EqualTo(1f).Within(1e-4f));
        dwell.Update("pressure_line", 0.1f);
        dwell.Update(null, 0.1f);
        Assert.That(dwell.PartId, Is.EqualTo("pressure_gauge"), "still the same look");
        Assert.That(dwell.Seconds, Is.EqualTo(1f).Within(1e-4f), "the slip does not count, but does not reset");
        dwell.Update("pressure_gauge", 0.1f);
        Assert.That(dwell.Seconds, Is.EqualTo(1.1f).Within(1e-4f));
    }

    [Test]
    public void Longer_look_elsewhere_switches()
    {
        var dwell = new DwellTracker();
        dwell.Update("pressure_gauge", 1f);
        for (var i = 0; i < 6; i++) dwell.Update("pump", 0.1f); // 0.6 s > grace
        Assert.That(dwell.PartId, Is.EqualTo("pump"));
        Assert.That(dwell.Seconds, Is.EqualTo(0f).Within(1e-4f).Or.EqualTo(0.1f).Within(1e-4f));
        dwell.Update(null, 1f);
        Assert.That(dwell.PartId, Is.EqualTo("pump"), "grace applies to looking at nothing too");
        dwell.Update(null, 1f);
        Assert.That(dwell.PartId, Is.Null);
    }
}
