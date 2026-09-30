using NUnit.Framework;
using UnityEditor;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>Device test 2026-09-30: the app vanished outside the Guardian circle; the boundary must be suppressible.</summary>
public class BoundaryTests
{
    [Test]
    public void Boundary_visibility_feature_is_enabled_for_android()
    {
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        var feature = settings != null ? settings.GetFeature<BoundaryVisibilityFeature>() : null;
        Assert.That(feature != null && feature.enabled, "Meta Quest: Boundary Visibility must be on (Fieldmate → Configure Project for Quest)");
    }
}
