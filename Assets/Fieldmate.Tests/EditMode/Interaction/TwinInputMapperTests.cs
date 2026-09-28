using Fieldmate.Interaction;
using Fieldmate.Twin;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Interaction;

public class TwinInputMapperTests
{
    [Test]
    public void Breaker_PowersTheMotorOnlyWhenOn()
    {
        var running = MachineInputs.Running;
        Assert.That(TwinInputMapper.Apply(running, "main_breaker", "off", 0.66f).Powered, Is.False);
        Assert.That(TwinInputMapper.Apply(running, "main_breaker", "locked", 1f).Powered, Is.False);
        Assert.That(TwinInputMapper.Apply(MachineInputs.Isolated, "main_breaker", "on", 0f).Powered, Is.True);
    }

    [Test]
    public void Valves_FollowTheirPosition()
    {
        var inputs = TwinInputMapper.Apply(MachineInputs.Running, "inlet_valve", null, 1f);
        Assert.That(inputs.InletOpening, Is.EqualTo(0f), "fully turned = closed");
        inputs = TwinInputMapper.Apply(inputs, "outlet_valve", null, 0.25f);
        Assert.That(inputs.OutletOpening, Is.EqualTo(0.75f).Within(1e-5f));
    }

    [Test]
    public void OtherParts_DoNotChangeTheTwin()
    {
        Assert.That(TwinInputMapper.Apply(MachineInputs.Running, "pump_cover", "removed", 1f), Is.EqualTo(MachineInputs.Running));
    }

    [Test]
    public void LockoutAndClosedInlet_IsolateThePump_SoPressureFallsForTheGaugeStep()
    {
        var model = new TelemetryModel(FaultModel.CreateDefault());
        var inputs = TwinInputMapper.Apply(model.Inputs, "main_breaker", "locked", 1f);
        model.Inputs = TwinInputMapper.Apply(inputs, "inlet_valve", "closed", 1f);
        for (var i = 0; i < 60 * 30; i++)
        {
            model.Step(1f / 60f);
        }

        Assert.That(model.IsIsolated, Is.True, "verify-zero step needs the gauge near 0 bar");
    }
}
