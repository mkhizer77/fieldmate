using Fieldmate.Interaction;
using Fieldmate.XR;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.XR;

public class InputWordsTests
{
    [Test]
    public void Hands_Pinch_Controllers_UseButtons()
    {
        Assert.That(InputWords.Grab(Modality.Hands), Is.EqualTo("pinch"));
        Assert.That(InputWords.Grab(Modality.Controllers), Is.EqualTo("grip"));
        Assert.That(InputWords.Talk(Modality.Controllers), Is.EqualTo("Hold X to talk"));
        Assert.That(InputWords.Place(Modality.Controllers), Does.Contain("trigger to place"));
        Assert.That(InputWords.Place(Modality.Hands), Does.Contain("pinch to place"));
    }

    [Test]
    public void ControlTag_ShowsTitle_Purpose_AndState()
    {
        Assert.That(ControlTagText.Format("Main breaker", "pump motor power", "on"), Is.EqualTo("Main breaker\n<size=26>pump motor power · ON</size>"));
        Assert.That(ControlTagText.Format("New relief cartridge", "fits the relief seat", null), Is.EqualTo("New relief cartridge\n<size=26>fits the relief seat</size>"));
        Assert.That(ControlTagText.Format("Gauge", null, null), Is.EqualTo("Gauge"));
    }
}
