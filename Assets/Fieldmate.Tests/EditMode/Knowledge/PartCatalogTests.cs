using System;
using Fieldmate.Knowledge;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Knowledge;

public class PartCatalogTests
{
    [Test]
    public void FromManual_IndexesPartsInOrder()
    {
        var catalog = PartCatalog.FromManual(MachineManual.Parse(MachineManualTests.Sample));

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.All[0].Id, Is.EqualTo("valve"));
        Assert.That(catalog.Contains("breaker"), Is.True);
        Assert.That(catalog.Contains(null), Is.False);
        Assert.That(catalog.TryGet("valve", out var valve) && valve.Name == "Valve", Is.True);
        Assert.That(catalog.TryGet(null, out _), Is.False);
        Assert.That(catalog.DisplayName("breaker"), Is.EqualTo("Breaker"));
        Assert.That(catalog.DisplayName("ghost"), Is.EqualTo("ghost"));
        Assert.That(catalog.DisplayName(null), Is.Empty);
    }

    [Test]
    public void Constructor_RejectsDuplicatesAndMissingIds()
    {
        var part = new PartInfo("p", "P", "", "");

        Assert.Throws<ArgumentException>(() => new PartCatalog(new[] { part, part }));
        Assert.Throws<ArgumentException>(() => new PartCatalog(new PartInfo[] { null }));
        Assert.Throws<ArgumentException>(() => new PartCatalog(new[] { new PartInfo(" ", "x", "", "") }));
        Assert.Throws<ArgumentNullException>(() => new PartCatalog(null));
        Assert.Throws<ArgumentNullException>(() => PartCatalog.FromManual(null));
    }
}
