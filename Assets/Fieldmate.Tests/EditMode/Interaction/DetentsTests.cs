using System;
using Fieldmate.Interaction;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Interaction;

public class DetentsTests
{
    private static Detents Breaker() => new(new[] { 0f, 90f, 135f }, new[] { "on", "off", "locked" }, 45f, 0f);

    [Test]
    public void StartsInTheDetentItStartsAt()
    {
        Assert.That(Breaker().State, Is.EqualTo("on"));
    }

    [Test]
    public void ReachingADetent_ReportsItOnce()
    {
        var detents = Breaker();
        Assert.That(detents.Update(50f, out _, out _), Is.False);
        Assert.That(detents.Update(88f, out var reached, out _), Is.True);
        Assert.That(reached, Is.EqualTo("off"));
        Assert.That(detents.Update(91f, out _, out _), Is.False, "still off: not reported again");
        Assert.That(detents.State, Is.EqualTo("off"));
    }

    [Test]
    public void BetweenDetents_KeepsTheLastState()
    {
        var detents = Breaker();
        detents.Update(90f, out _, out _);
        detents.Update(110f, out _, out _);
        Assert.That(detents.State, Is.EqualTo("off"));
    }

    [Test]
    public void TicksFireWhenCrossingTickAngles()
    {
        var detents = new Detents(new[] { 0f, 1080f }, new[] { "open", "closed" }, 45f, 0f);
        detents.Update(30f, out _, out var first);
        detents.Update(50f, out _, out var second);
        Assert.That(first, Is.False);
        Assert.That(second, Is.True);
    }

    [Test]
    public void ReleaseSnapsToANearbyDetent_OnlyWhenClose()
    {
        var detents = Breaker();
        Assert.That(detents.SnapTarget(78f), Is.EqualTo(90f));
        Assert.That(detents.SnapTarget(130f), Is.EqualTo(135f));
        Assert.That(detents.SnapTarget(45f), Is.EqualTo(45f), "far from every detent: stays");
    }

    [Test]
    public void MismatchedStates_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Detents(new[] { 0f, 90f }, new[] { "open" }, 30f, 0f));
    }
}
