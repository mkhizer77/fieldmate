using System;
using System.Collections.Generic;
using Fieldmate.Twin;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Twin;

public class FaultModelTests
{
    private FaultModel faults;
    private List<(string id, bool active)> changes;

    [SetUp]
    public void SetUp()
    {
        faults = FaultModel.CreateDefault();
        changes = new List<(string, bool)>();
        faults.FaultChanged += (id, active) => changes.Add((id, active));
    }

    [Test]
    public void Default_HasThreeKnownFaults_NoneActive()
    {
        Assert.That(faults.Definitions, Has.Count.EqualTo(3));
        Assert.That(faults.IsKnown(FaultModel.Overpressure), Is.True);
        Assert.That(faults.IsKnown(FaultModel.Overheating), Is.True);
        Assert.That(faults.IsKnown(FaultModel.LooseMount), Is.True);
        Assert.That(faults.IsKnown("bogus"), Is.False);
        Assert.That(faults.ActiveCount, Is.Zero);
    }

    [Test]
    public void Inject_ActivatesOnce_AndRaisesEvent()
    {
        Assert.That(faults.Inject(FaultModel.Overpressure), Is.True);
        Assert.That(faults.Inject(FaultModel.Overpressure), Is.False, "already active");
        Assert.That(faults.Inject("bogus"), Is.False);

        Assert.That(faults.IsActive(FaultModel.Overpressure), Is.True);
        Assert.That(faults.ActiveCount, Is.EqualTo(1));
        Assert.That(changes, Is.EqualTo(new[] { (FaultModel.Overpressure, true) }));
    }

    [Test]
    public void Clear_And_ClearAll_DeactivateWithEvents()
    {
        faults.Inject(FaultModel.Overpressure);
        faults.Inject(FaultModel.LooseMount);

        Assert.That(faults.Clear(FaultModel.Overpressure), Is.True);
        Assert.That(faults.Clear(FaultModel.Overpressure), Is.False);
        faults.ClearAll();

        Assert.That(faults.ActiveCount, Is.Zero);
        Assert.That(changes, Is.EqualTo(new[]
        {
            (FaultModel.Overpressure, true), (FaultModel.LooseMount, true),
            (FaultModel.Overpressure, false), (FaultModel.LooseMount, false),
        }));
    }

    [Test]
    public void TryRepair_RequiresIsolation()
    {
        faults.Inject(FaultModel.Overpressure);

        Assert.That(faults.TryRepair("replace_relief_cartridge", machineIsolated: false), Is.EqualTo(RepairOutcome.NotIsolated));
        Assert.That(faults.IsActive(FaultModel.Overpressure), Is.True);

        Assert.That(faults.TryRepair("replace_relief_cartridge", machineIsolated: true), Is.EqualTo(RepairOutcome.Fixed));
        Assert.That(faults.IsActive(FaultModel.Overpressure), Is.False);
    }

    [Test]
    public void TryRepair_ReportsNothingToRepairAndUnknownAction()
    {
        Assert.That(faults.TryRepair("tighten_mount_bolts", true), Is.EqualTo(RepairOutcome.NothingToRepair));
        Assert.That(faults.TryRepair("hit_it_with_a_hammer", true), Is.EqualTo(RepairOutcome.UnknownAction));
        Assert.That(faults.TryRepair(null, true), Is.EqualTo(RepairOutcome.UnknownAction));
    }

    [Test]
    public void TryGetDefinition_ReturnsSymptomAndRepair()
    {
        Assert.That(faults.TryGetDefinition(FaultModel.LooseMount, out var definition), Is.True);
        Assert.That(definition.RepairActionId, Is.EqualTo("tighten_mount_bolts"));
        Assert.That(definition.Symptom, Does.Contain("Vibration"));
        Assert.That(definition.Name, Is.Not.Empty);

        Assert.That(faults.TryGetDefinition("bogus", out var missing), Is.False);
        Assert.That(missing, Is.Null);
    }

    [Test]
    public void CombinedEffect_SumsActiveFaultsOnly()
    {
        Assert.That(faults.CombinedEffect().Current, Is.Zero);

        faults.Inject(FaultModel.Overpressure);
        faults.Inject(FaultModel.Overheating);
        var effect = faults.CombinedEffect();

        Assert.That(effect.Current, Is.EqualTo(4.5f).Within(1e-5f));
        Assert.That(effect.Temperature, Is.EqualTo(45f).Within(1e-5f));
        Assert.That(effect.Vibration, Is.Zero);
        Assert.That(effect.DefeatsReliefValve, Is.True);
    }

    [Test]
    public void Constructor_RejectsNullAndDuplicates()
    {
        var a = new FaultDefinition("a", "A", "s", "fix_a", default);

        Assert.Throws<ArgumentNullException>(() => new FaultModel(null));
        Assert.Throws<ArgumentException>(() => new FaultModel(new[] { a, a }));
        Assert.Throws<ArgumentException>(() => new FaultModel(new FaultDefinition[] { null }));
    }

    [Test]
    public void FaultDefinition_ValidatesIdsAndDefaultsName()
    {
        Assert.Throws<ArgumentException>(() => new FaultDefinition(" ", "n", "s", "fix", default));
        Assert.Throws<ArgumentException>(() => new FaultDefinition("id", "n", "s", "", default));

        var definition = new FaultDefinition("id", null, null, "fix", new FaultEffect(pressure: 1f));
        Assert.That(definition.Name, Is.EqualTo("id"));
        Assert.That(definition.Symptom, Is.Empty);
        Assert.That(definition.Effect.Pressure, Is.EqualTo(1f));
    }
}
