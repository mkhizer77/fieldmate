using System.Collections.Generic;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;

namespace Fieldmate.XR;

/// <summary>
/// Where a hand's pose comes from (#80, XR_EXT_hand_tracking_data_source): the cameras, or the controller it holds.
/// While controllers are held the runtime still reports hands, posed around the controllers from their touch sensors
/// (as Meta's home shows them); those hands are shown, but they mean the user is on controllers.
/// </summary>
public static class HandDataSource
{
    private static readonly List<XRHandSubsystem> Subsystems = new();
    private static XRHandSubsystem subsystem;

    /// <summary>True when the runtime says this hand's pose is derived from a held controller.</summary>
    public static bool IsFromController(Handedness handedness)
    {
        if (subsystem == null || !subsystem.running)
        {
            Subsystems.Clear();
            UnityEngine.SubsystemManager.GetSubsystems(Subsystems);
            subsystem = Subsystems.Count > 0 ? Subsystems[0] : null;
        }

        return subsystem != null
               && subsystem.TryGetExtendedData<UnityEngine.XR.Hands.OpenXR.HandTrackingDataSource>(handedness, out var source)
               && source == UnityEngine.XR.Hands.OpenXR.HandTrackingDataSource.Controller;
    }
}
