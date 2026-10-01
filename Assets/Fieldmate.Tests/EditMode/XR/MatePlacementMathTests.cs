using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>#71: where the mate goes while the user points it into place.</summary>
public class MatePlacementMathTests
{
    private const float Eyes = 1.65f;
    private static readonly Ray Ahead = new(new Vector3(0.2f, 1.3f, 0f), new Vector3(0f, -0.2f, 1f));

    [Test]
    public void A_table_in_reach_takes_it()
    {
        var table = new Vector3(0.2f, 0.75f, 1.2f);
        Assert.That(MatePlacementMath.Target(Ahead, true, table, Vector3.up, Eyes), Is.EqualTo(table));
    }

    [Test]
    public void The_floor_or_a_wall_does_not_it_floats_short_of_them()
    {
        var floor = MatePlacementMath.Target(Ahead, true, new Vector3(0.2f, 0f, 1.6f), Vector3.up, Eyes);
        Assert.That(floor.y, Is.InRange(Eyes - 0.75f, Eyes - 0.25f));
        var wall = MatePlacementMath.Target(Ahead, true, new Vector3(0.2f, 1.1f, 0.9f), Vector3.back, Eyes);
        Assert.That(Vector3.Distance(wall, Ahead.origin), Is.InRange(0.6f, 0.9f), "short of the wall");
    }

    [Test]
    public void Nothing_hit_floats_a_metre_away_below_the_eyes()
    {
        var p = MatePlacementMath.Target(new Ray(new Vector3(0f, 1.3f, 0f), Vector3.up + Vector3.forward), false, default, default, Eyes);
        Assert.That(p.y, Is.EqualTo(Eyes - 0.25f).Within(1e-4f), "pointing up never lifts it over the head");
        var far = MatePlacementMath.Target(Ahead, true, new Vector3(0.2f, 0.8f, 6f), Vector3.up, Eyes);
        Assert.That(Vector3.Distance(far, Ahead.origin), Is.LessThanOrEqualTo(1.6f + 0.6f), "a far table is out of reach: it floats");
    }
}
