using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>#57: the machine's size follows the free floor around where it is placed.</summary>
public class MachineFitTests
{
    private const int Rays = 32;

    // Where probe rays from the pivot hit a rectangular room [left, right] x [back, front] (machine frame), within 3 m.
    private static Vector2[] Room(float left, float right, float back, float front)
    {
        var hits = new Vector2[Rays];
        for (var i = 0; i < Rays; i++)
        {
            var d = MachineFit.Direction(i, Rays);
            var t = 3f;
            if (d.x > 1e-3f) t = Mathf.Min(t, right / d.x);
            if (d.x < -1e-3f) t = Mathf.Min(t, -left / -d.x);
            if (d.y > 1e-3f) t = Mathf.Min(t, front / d.y);
            if (d.y < -1e-3f) t = Mathf.Min(t, -back / -d.y);
            hits[i] = d * t;
        }

        return hits;
    }

    private static float Fit(Vector2[] hits, out bool tight) => MachineFit.Scale(hits, hits.Length, out tight);

    [Test]
    public void Nothing_near_gets_full_size()
    {
        Assert.That(MachineFit.Scale(new Vector2[0], 0, out var tight), Is.EqualTo(1f));
        Assert.That(tight, Is.False);
        Assert.That(Fit(Room(-5f, 5f, -5f, 5f), out _), Is.EqualTo(1f));
    }

    [Test]
    public void A_narrow_room_shrinks_it_so_you_can_still_pass_the_ends()
    {
        var scale = Fit(Room(-1.4f, 1.2f, -1f, 2f), out var tight);
        Assert.That(scale, Is.InRange(0.6f, 0.95f));
        Assert.That(tight, Is.False);
        Assert.That(scale * 1.45f + MachineFit.SideClearance, Is.LessThanOrEqualTo(1.4f + 1e-4f), "the cabinet end keeps 35 cm to the wall");
    }

    [Test]
    public void A_wall_close_in_front_matters_more_than_one_behind()
    {
        Assert.That(Fit(Room(-5f, 5f, -5f, 1.2f), out _), Is.LessThan(1f), "the user needs 0.9 m in front to work");
        Assert.That(Fit(Room(-5f, 5f, -0.55f, 5f), out _), Is.EqualTo(1f), "it may stand close to a wall behind");
    }

    [Test]
    public void No_room_at_all_clamps_to_the_minimum_and_says_so()
    {
        Assert.That(Fit(Room(-0.6f, 0.6f, -0.3f, 0.6f), out var tight), Is.EqualTo(MachineFit.MinScale));
        Assert.That(tight, Is.True);
    }
}
