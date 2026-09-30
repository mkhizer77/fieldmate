using Fieldmate.AI;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>Device 2026-09-30: "where is the breaker?" got "behind you or to the side"; the context now says which.</summary>
public class SpatialHintsTests
{
    [Test]
    public void Directions_relative_to_the_head()
    {
        var head = new Vector3(0f, 1.6f, 0f);
        Assert.That(SpatialHints.Describe(head, Vector3.forward, new Vector3(0f, 1.2f, 2f)), Is.EqualTo("2.0 m away, straight ahead"));
        Assert.That(SpatialHints.Describe(head, Vector3.forward, new Vector3(1.6f, 1.6f, 0.1f)), Does.StartWith("1.6 m away, to your right"));
        Assert.That(SpatialHints.Describe(head, Vector3.forward, new Vector3(-1f, 1.2f, -1.2f)), Does.Contain("behind you to the left"));
        Assert.That(SpatialHints.Describe(head, Vector3.forward, new Vector3(0f, 0.6f, 1f)), Does.EndWith(", low down"));
    }
}
