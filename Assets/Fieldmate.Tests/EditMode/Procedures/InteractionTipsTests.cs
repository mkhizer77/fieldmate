using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Procedures;

/// <summary>Device test 2026-09-30: a first-time tip per interaction kind, worded for the active input.</summary>
public class InteractionTipsTests
{
    [Test]
    public void One_key_per_kind_of_interaction()
    {
        Assert.That(InteractionTips.Key(StepDefinition.Operate("a", "t", "outlet_valve", "closed"), 1), Is.EqualTo("turn"));
        Assert.That(InteractionTips.Key(StepDefinition.Operate("b", "t", "main_breaker", "locked"), 2), Is.EqualTo("turn2"));
        Assert.That(InteractionTips.Key(StepDefinition.Operate("c", "t", "pump_cover", "removed"), 1), Is.EqualTo("pull"));
        Assert.That(InteractionTips.Key(StepDefinition.Tool("d", "t", "seat", "cartridge"), 1), Is.EqualTo("tool"));
        Assert.That(InteractionTips.Key(StepDefinition.Inspect("e", "t", "relief_valve", 1f), 1), Is.EqualTo("look"));
        Assert.That(InteractionTips.Key(StepDefinition.Measure("f", "t", "gauge", 0f, 0.2f, "bar"), 1), Is.Null, "reading needs no tip");
        Assert.That(InteractionTips.Key(null, 1), Is.Null);
    }

    [Test]
    public void Tips_name_the_gesture_for_the_input()
    {
        var turn = StepDefinition.Operate("a", "t", "outlet_valve", "closed");
        Assert.That(InteractionTips.For(turn, 1, Modality.Hands), Does.Contain("pinch thumb and index").And.Contain("around the pivot"));
        Assert.That(InteractionTips.For(turn, 1, Modality.Controllers), Does.Contain("hold the grip").And.Not.Contain("pinch"));
        var breaker = StepDefinition.Operate("b", "t", "main_breaker", "locked");
        Assert.That(InteractionTips.For(breaker, 2, Modality.Hands), Does.Contain("both hands").And.Contain("steering wheel"));
        Assert.That(InteractionTips.For(turn, 1, Modality.Hands), Does.StartWith("Tip:"));
    }
}
