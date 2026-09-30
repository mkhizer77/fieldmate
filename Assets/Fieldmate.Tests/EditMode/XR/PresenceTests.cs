using Fieldmate.Editor;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>#55: the hand and controller presence prefabs are complete and use the glow shader.</summary>
public class PresenceTests
{
    [Test]
    public void Glow_shader_and_material_exist()
    {
        Assert.That(Shader.Find(PresenceBuilder.ShaderName), Is.Not.Null);
        var material = AssetDatabase.LoadAssetAtPath<Material>(PresenceBuilder.MaterialPath);
        Assert.That(material, Is.Not.Null, "run Fieldmate → Build Presence Prefabs");
        Assert.That(material.shader.name, Is.EqualTo(PresenceBuilder.ShaderName));
        var glow = material.GetColor("_GlowColor");
        Assert.That(glow.r, Is.GreaterThan(0.9f).And.GreaterThan(glow.b), "light yellow");
        Assert.That(material.GetFloat("_Fill"), Is.EqualTo(0f), "outline only, no glowing fill (device test 2026-09-30)");
        Assert.That(material.GetFloat("_RimPower"), Is.GreaterThanOrEqualTo(3f), "thin rim");
    }

    [TestCase(PresenceBuilder.LeftHandPrefabPath, Handedness.Left)]
    [TestCase(PresenceBuilder.RightHandPrefabPath, Handedness.Right)]
    public void Hand_prefab_drives_every_joint_and_hides_until_tracked(string path, Handedness handedness)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        Assert.That(prefab.GetComponent<XRHandTrackingEvents>().handedness, Is.EqualTo(handedness));
        var driver = prefab.GetComponent<XRHandSkeletonDriver>();
        Assert.That(driver.jointTransformReferences.Count, Is.EqualTo(XRHandJointID.EndMarker.ToIndex()), "all 26 joints mapped");
        foreach (var joint in driver.jointTransformReferences)
        {
            Assert.That(joint.jointTransform, Is.Not.Null, joint.xrHandJointID.ToString());
        }

        var renderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
        Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo(PresenceBuilder.ShaderName));
        Assert.That(renderer.enabled, Is.False, "shown by XRHandMeshController once tracking is acquired");
        Assert.That(prefab.GetComponent<XRHandMeshController>().handMeshRenderer, Is.SameAs(renderer));
        Assert.That(prefab.GetComponent<PresenceGlow>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<TrackedHandMesh>(), Is.Not.Null, "the runtime's fitted mesh takes over from the sample model");
    }

    [Test]
    public void Hand_mesh_feature_is_enabled_for_android()
    {
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        Assert.That(settings, Is.Not.Null);
        var feature = settings.GetFeature<MetaOpenXRHandMeshData>();
        Assert.That(feature != null && feature.enabled, "Meta Quest: Hand Mesh Data must be on (Fieldmate → Configure Project for Quest)");
    }

    [Test]
    public void Controller_prefab_shows_only_on_controllers()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PresenceBuilder.ControllerPrefabPath);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<ModalityVisibility>().ShownFor, Is.EqualTo(Modality.Controllers));
        Assert.That(prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial.shader.name, Is.EqualTo(PresenceBuilder.ShaderName));
    }
}
