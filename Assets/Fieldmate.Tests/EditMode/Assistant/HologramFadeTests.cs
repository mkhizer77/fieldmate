using Fieldmate.Assistant;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Assistant;

/// <summary>#98: the fade must land exactly on its target, not stop "approximately" short of it.</summary>
public class HologramFadeTests
{
    [TestCase(0.9999999f, 1f)]
    [TestCase(1e-7f, 0f)]
    public void Fade_FinishesExactlyOnTarget(float start, float target)
    {
        var presence = start;
        var steps = 0;
        while (HologramMate.StepFade(ref presence, target, 1f / 72f) && steps < 1000)
        {
            steps++;
        }

        Assert.That(presence, Is.EqualTo(target));
    }

    [Test]
    public void Fade_IsGradual_AndSettles()
    {
        var presence = 0f;
        Assert.That(HologramMate.StepFade(ref presence, 1f, 0.1f), Is.True);
        Assert.That(presence, Is.InRange(0.01f, 0.2f), "materialises over time, does not pop in");
        Assert.That(HologramMate.StepFade(ref presence, 1f, 10f), Is.True);
        Assert.That(presence, Is.EqualTo(1f));
        Assert.That(HologramMate.StepFade(ref presence, 1f, 0.1f), Is.False, "nothing to do once there");
    }
}
