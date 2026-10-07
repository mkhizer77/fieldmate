using Fieldmate.AI;
using Fieldmate.Procedures;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>#90: the breaker was held five times and the mate said nothing after the first.</summary>
public class SafetyLineGateTests
{
    private static readonly SafetyRule Outlet = new("outlet_before_power", "Open the outlet first.", "main_breaker", "outlet_valve", "open");
    private static readonly SafetyRule Inlet = new("inlet_before_power", "Open the inlet first.", "main_breaker", "inlet_valve", "open");

    [Test]
    public void Every_held_attempt_is_spoken_but_not_the_same_rule_twice_in_a_few_seconds()
    {
        var gate = new SafetyLineGate();
        Assert.That(gate.ShouldSpeak(Outlet, 10.0), Is.True, "first attempt");
        Assert.That(gate.ShouldSpeak(Outlet, 12.0), Is.False, "retrying straight away: once is enough");
        Assert.That(gate.ShouldSpeak(Outlet, 10.0 + SafetyLineGate.RepeatSeconds + 0.1), Is.True, "trying again later gets a reminder");
        Assert.That(gate.ShouldSpeak(Inlet, 19.0), Is.True, "a different rule is news");
    }

    [Test]
    public void One_attempt_breaking_two_rules_gets_one_line()
    {
        var gate = new SafetyLineGate();
        Assert.That(gate.ShouldSpeak(Inlet, 5.0), Is.True);
        Assert.That(gate.ShouldSpeak(Outlet, 5.0), Is.False, "same attempt");
        gate.Reset();
        Assert.That(gate.ShouldSpeak(Inlet, 5.5), Is.True, "a new run starts fresh");
        Assert.That(gate.ShouldSpeak(null, 9.0), Is.False);
    }
}
