using Fieldmate.XR;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>Device 2026-09-30: controllers lying nearby stay tracked while the hands come up; hands must win.</summary>
public class ModalityDecisionTests
{
    [Test]
    public void Tracked_hands_win_controllers_only_without_hands_else_stay()
    {
        Assert.That(ModalityDecision.Wanted(Modality.Controllers, handsTracked: true, controllersTracked: true), Is.EqualTo(Modality.Hands));
        Assert.That(ModalityDecision.Wanted(Modality.Hands, handsTracked: false, controllersTracked: true), Is.EqualTo(Modality.Controllers));
        Assert.That(ModalityDecision.Wanted(Modality.Controllers, handsTracked: false, controllersTracked: false), Is.EqualTo(Modality.Controllers), "nothing tracked: keep");
        Assert.That(ModalityDecision.Wanted(Modality.Hands, handsTracked: false, controllersTracked: false), Is.EqualTo(Modality.Hands));
    }
}
