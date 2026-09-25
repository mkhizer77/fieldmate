using System;
using Fieldmate.Twin;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Twin;

public class TelemetryModelTests
{
    private const float Eps = 1e-3f;

    private static TelemetryModel Running() => new(FaultModel.CreateDefault());

    private static void Run(TelemetryModel model, float seconds, float dt = 1f / 72f)
    {
        for (var t = 0f; t < seconds; t += dt)
        {
            model.Step(dt);
        }
    }

    [Test]
    public void StartsSettled_AtNominalValues()
    {
        var model = Running();

        Assert.That(model[TelemetryChannel.Pressure], Is.EqualTo(TelemetryModel.NominalPressure).Within(Eps));
        Assert.That(model[TelemetryChannel.Temperature], Is.EqualTo(40f).Within(Eps));
        Assert.That(model[TelemetryChannel.Vibration], Is.EqualTo(2f).Within(Eps));
        Assert.That(model[TelemetryChannel.Current], Is.EqualTo(8f).Within(Eps));
        Assert.That(model.WorstStatus(), Is.EqualTo(ChannelStatus.Normal));
        Assert.That(model.ElapsedSeconds, Is.Zero);
    }

    [Test]
    public void Step_FollowsFirstOrderResponse()
    {
        var model = Running();
        model.Inputs = MachineInputs.Isolated;
        var tau = TelemetryModel.Spec(TelemetryChannel.Pressure).TimeConstantSeconds;

        model.Step(tau);

        // After one time constant, 63.2% of the way from 4 bar to 0 bar.
        Assert.That(model[TelemetryChannel.Pressure], Is.EqualTo(4f * MathF.Exp(-1f)).Within(Eps));
        Assert.That(model.ElapsedSeconds, Is.EqualTo(tau).Within(1e-6));
    }

    [Test]
    public void Step_IsIndependentOfStepSize()
    {
        var coarse = Running();
        var fine = Running();
        coarse.Inputs = fine.Inputs = MachineInputs.Running.WithOutlet(0.2f);

        coarse.Step(1f);
        for (var i = 0; i < 100; i++)
        {
            fine.Step(0.01f);
        }

        for (var c = 0; c < TelemetryModel.ChannelCount; c++)
        {
            var channel = (TelemetryChannel)c;
            Assert.That(fine[channel], Is.EqualTo(coarse[channel]).Within(Eps), channel.ToString());
        }
    }

    [Test]
    public void IsDeterministic()
    {
        var a = Running();
        var b = Running();
        foreach (var model in new[] { a, b })
        {
            model.Faults.Inject(FaultModel.LooseMount);
            Run(model, 3f);
            model.Inputs = MachineInputs.Running.WithLoad(1f);
            Run(model, 2f);
        }

        for (var c = 0; c < TelemetryModel.ChannelCount; c++)
        {
            Assert.That(b[(TelemetryChannel)c], Is.EqualTo(a[(TelemetryChannel)c]));
        }
    }

    [TestCase(-0.1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Step_RejectsInvalidDelta(float dt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Running().Step(dt));
    }

    [Test]
    public void Step_ZeroDelta_ChangesNothing()
    {
        var model = Running();
        model.Inputs = MachineInputs.Isolated;
        model.Step(0f);

        Assert.That(model[TelemetryChannel.Pressure], Is.EqualTo(TelemetryModel.NominalPressure).Within(Eps));
        Assert.That(model.ElapsedSeconds, Is.Zero);
    }

    [Test]
    public void Overpressure_RaisesPressureAlarm_ThenRepairAfterIsolationRestoresNormal()
    {
        var model = Running();
        model.Faults.Inject(FaultModel.Overpressure);
        Run(model, 5f);

        Assert.That(model.Status(TelemetryChannel.Pressure), Is.EqualTo(ChannelStatus.Alarm));
        Assert.That(model.TryRepair("replace_relief_cartridge"), Is.EqualTo(RepairOutcome.NotIsolated));

        model.Inputs = MachineInputs.Isolated;
        Run(model, 5f);
        Assert.That(model.IsIsolated, Is.True);
        Assert.That(model.TryRepair("replace_relief_cartridge"), Is.EqualTo(RepairOutcome.Fixed));

        model.Inputs = MachineInputs.Running;
        Run(model, 5f);
        Assert.That(model.Status(TelemetryChannel.Pressure), Is.EqualTo(ChannelStatus.Normal));
    }

    [Test]
    public void ReliefValve_CapsThrottledPressure_WhenHealthy()
    {
        var model = Running();
        model.Inputs = MachineInputs.Running.WithOutlet(0f);

        Assert.That(model.Target(TelemetryChannel.Pressure), Is.EqualTo(TelemetryModel.ReliefSetPoint).Within(Eps));

        model.Faults.Inject(FaultModel.Overpressure);
        Assert.That(model.Target(TelemetryChannel.Pressure), Is.GreaterThan(TelemetryModel.ReliefSetPoint));
    }

    [Test]
    public void Overheating_AndLooseMount_AlarmTheirChannels()
    {
        var model = Running();
        model.Faults.Inject(FaultModel.Overheating);
        model.Faults.Inject(FaultModel.LooseMount);
        Run(model, 120f, dt: 0.1f);

        Assert.That(model.Status(TelemetryChannel.Temperature), Is.EqualTo(ChannelStatus.Alarm));
        Assert.That(model.Status(TelemetryChannel.Vibration), Is.EqualTo(ChannelStatus.Alarm));
        Assert.That(model.WorstStatus(), Is.EqualTo(ChannelStatus.Alarm));
    }

    [Test]
    public void IsIsolated_NeedsBreakerOpenAndPressureBledOff()
    {
        var model = Running();
        Assert.That(model.IsIsolated, Is.False);

        model.Inputs = MachineInputs.Running.WithPowered(false);
        Assert.That(model.IsIsolated, Is.False, "pressure still high right after lockout");

        Run(model, 5f);
        Assert.That(model.IsIsolated, Is.True);
    }

    [Test]
    public void ClosingInlet_WhilePowered_DropsPressureButNotIsolated()
    {
        var model = Running();
        model.Inputs = MachineInputs.Running.WithInlet(0f);
        Run(model, 5f);

        Assert.That(model[TelemetryChannel.Pressure], Is.LessThan(TelemetryModel.IsolationPressure));
        Assert.That(model.IsIsolated, Is.False, "breaker still closed");
    }

    [Test]
    public void Settle_JumpsToTargetsAndResetsClock()
    {
        var model = Running();
        model.Inputs = MachineInputs.Isolated;
        model.Step(0.1f);
        model.Settle();

        Assert.That(model[TelemetryChannel.Pressure], Is.Zero);
        Assert.That(model[TelemetryChannel.Temperature], Is.EqualTo(TelemetryModel.AmbientTemperature).Within(Eps));
        Assert.That(model.ElapsedSeconds, Is.Zero);
    }

    [Test]
    public void Constructor_WithInputs_SettlesToThoseInputs()
    {
        var model = new TelemetryModel(FaultModel.CreateDefault(), MachineInputs.Isolated);

        Assert.That(model.IsIsolated, Is.True);
        Assert.That(model.Inputs, Is.EqualTo(MachineInputs.Isolated));
        Assert.Throws<ArgumentNullException>(() => new TelemetryModel(null));
    }

    [Test]
    public void Spec_And_Classify_UseThresholds()
    {
        var pressure = TelemetryModel.Spec(TelemetryChannel.Pressure);

        Assert.That(pressure.Unit, Is.EqualTo("bar"));
        Assert.That(pressure.Classify(4.9f), Is.EqualTo(ChannelStatus.Normal));
        Assert.That(pressure.Classify(5f), Is.EqualTo(ChannelStatus.Warning));
        Assert.That(pressure.Classify(6f), Is.EqualTo(ChannelStatus.Alarm));
        Assert.Throws<ArgumentOutOfRangeException>(() => TelemetryModel.Spec((TelemetryChannel)42));
    }
}
