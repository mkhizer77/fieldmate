using Fieldmate.XR;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>#71 device test: the pointer follows the controller or the hand, never a mix of both.</summary>
public class PointerPoseTests
{
    [Test]
    public void Holding_controllers_points_with_the_controller_even_if_a_hand_pose_still_reports()
    {
        Assert.That(PointerPose.UseController(Modality.Controllers, controllerTracked: true, handTracked: true), Is.True);
        Assert.That(PointerPose.UseController(Modality.Controllers, controllerTracked: false, handTracked: true), Is.False, "controller lost: the hand");
    }

    [Test]
    public void Hands_point_with_the_hand_and_fall_back_to_a_tracked_controller()
    {
        Assert.That(PointerPose.UseController(Modality.Hands, controllerTracked: true, handTracked: true), Is.False);
        Assert.That(PointerPose.UseController(Modality.Hands, controllerTracked: true, handTracked: false), Is.True);
    }
}
