using Fieldmate.XR;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>#57 part 1: ask for a room scan only when the room has no scene data after a short wait.</summary>
public class SceneScanTests
{
    [Test]
    public void Requests_only_without_room_data_after_the_wait()
    {
        Assert.That(SceneScan.ShouldRequest(0, 1f), Is.False, "give existing scene data time to load");
        Assert.That(SceneScan.ShouldRequest(0, SceneScan.WaitSeconds), Is.True);
        Assert.That(SceneScan.ShouldRequest(3, 10f), Is.False, "the room is already scanned");
    }
}
