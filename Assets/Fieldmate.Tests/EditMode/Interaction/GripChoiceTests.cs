using Fieldmate.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Interaction;

/// <summary>#86: a ray takes the grip point nearest to it; the second ray on a two-hand bar takes the free end.</summary>
public class GripChoiceTests
{
    private static readonly Vector3[] BarEnds = { new(-0.13f, 0f, 0f), new(0.13f, 0f, 0f) };

    [Test]
    public void The_point_nearest_the_ray_wins()
    {
        var origin = new Vector3(0.3f, 0f, -1.5f);
        Assert.That(GripChoice.Nearest(origin, Vector3.forward, BarEnds, null), Is.EqualTo(1), "aimed right of centre");
        Assert.That(GripChoice.Nearest(new Vector3(-0.3f, 0f, -1.5f), Vector3.forward, BarEnds, null), Is.EqualTo(0));
    }

    [Test]
    public void A_taken_point_is_skipped_and_none_free_is_minus_one()
    {
        var origin = new Vector3(0f, 0f, -1.5f);
        Assert.That(GripChoice.Nearest(origin, Vector3.forward, BarEnds, new[] { false, true }), Is.EqualTo(0), "the other end");
        Assert.That(GripChoice.Nearest(origin, Vector3.forward, BarEnds, new[] { true, true }), Is.EqualTo(-1));
        Assert.That(GripChoice.Nearest(origin, Vector3.forward, null, null), Is.EqualTo(-1));
    }

    [Test]
    public void Distance_is_perpendicular_to_the_ray_and_to_the_origin_behind_it()
    {
        Assert.That(GripChoice.DistanceToRay(Vector3.zero, Vector3.forward, new Vector3(0.2f, 0f, 3f)), Is.EqualTo(0.2f).Within(1e-5f));
        Assert.That(GripChoice.DistanceToRay(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -2f)), Is.EqualTo(2f).Within(1e-5f));
    }
}
