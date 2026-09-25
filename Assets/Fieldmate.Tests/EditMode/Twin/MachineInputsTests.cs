using Fieldmate.Twin;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Twin;

public class MachineInputsTests
{
    [Test]
    public void Constructor_ClampsOpeningsAndLoad()
    {
        var inputs = new MachineInputs(true, 1.5f, -2f, float.NaN);

        Assert.That(inputs.InletOpening, Is.EqualTo(1f));
        Assert.That(inputs.OutletOpening, Is.EqualTo(0f));
        Assert.That(inputs.Load, Is.EqualTo(0f));
    }

    [Test]
    public void Presets_DescribeRunningAndIsolated()
    {
        Assert.That(MachineInputs.Running.Powered, Is.True);
        Assert.That(MachineInputs.Running.InletOpening, Is.EqualTo(1f));
        Assert.That(MachineInputs.Isolated.Powered, Is.False);
        Assert.That(MachineInputs.Isolated.InletOpening, Is.EqualTo(0f));
        Assert.That(MachineInputs.Isolated.OutletOpening, Is.EqualTo(0f));
    }

    [Test]
    public void WithMethods_ChangeOneField()
    {
        var baseline = MachineInputs.Running;

        Assert.That(baseline.WithPowered(false), Is.EqualTo(new MachineInputs(false, 1f, 1f, 0.5f)));
        Assert.That(baseline.WithInlet(0.25f).InletOpening, Is.EqualTo(0.25f));
        Assert.That(baseline.WithOutlet(0.75f).OutletOpening, Is.EqualTo(0.75f));
        Assert.That(baseline.WithLoad(2f).Load, Is.EqualTo(1f));
    }

    [Test]
    public void Equality_AndHash_AreValueBased()
    {
        var a = new MachineInputs(true, 0.5f, 0.5f, 0.5f);
        var b = new MachineInputs(true, 0.5f, 0.5f, 0.5f);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals(null), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        Assert.That(a.ToString(), Does.Contain("inlet=0.5"));
    }
}
