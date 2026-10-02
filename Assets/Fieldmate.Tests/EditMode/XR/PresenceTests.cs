using System.Linq;
using System.Runtime.InteropServices;
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
        Assert.That(material.GetFloat("_Fill"), Is.EqualTo(0f), "free hands: outline only, no fill (device test 2026-09-30)");
        Assert.That(material.GetFloat("_OutlineWidth"), Is.EqualTo(0.004f).Within(1e-4f));
        Assert.That(material.shader.passCount, Is.EqualTo(3), "stencil mask, outline, fill while holding a controller (#80)");
        var lightMode = new UnityEngine.Rendering.ShaderTagId("LightMode");
        Assert.That(material.shader.FindPassTagValue(0, lightMode).name, Is.EqualTo("SRPDefaultUnlit").IgnoreCase, "URP draws it first");
        Assert.That(material.shader.FindPassTagValue(1, lightMode).name, Is.EqualTo("UniversalForward").IgnoreCase, "then the outline (untagged extra passes never draw on device)");
        Assert.That(material.shader.FindPassTagValue(2, lightMode).name, Is.EqualTo("UniversalForwardOnly").IgnoreCase, "then the fill");
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
    public void Controller_prefab_is_the_runtime_model_shown_only_on_controllers()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PresenceBuilder.ControllerPrefabPath);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<ModalityVisibility>().ShownFor, Is.EqualTo(Modality.Controllers));
        Assert.That(prefab.GetComponentInChildren<ControllerModel>(true), Is.Not.Null, "#80: the runtime's controller model, as in Meta's home");
        Assert.That(prefab.GetComponentsInChildren<MeshRenderer>(true), Is.Empty, "no ring or stand-in model in the repo");
    }

    [Test]
    public void Runtime_model_is_turned_from_gltf_into_the_grip_pose_space()
    {
        // A point on the controller in OpenXR grip space, as glTFast imports it (X mirrored), must land where Unity's
        // OpenXR pose puts it (Z mirrored). #86: unturned, the model faced backwards on the real controller.
        var openXr = new Vector3(0.01f, 0.02f, -0.05f); // right of, above and in front of the grip (-Z is forward)
        var imported = new Vector3(-openXr.x, openXr.y, openXr.z);
        var expected = new Vector3(openXr.x, openXr.y, -openXr.z);
        Assert.That(Vector3.Distance(ControllerModel.GltfToPose * imported, expected), Is.LessThan(1e-5f));
    }

    [Test]
    public void Runtime_models_and_controller_driven_hands_are_enabled_for_android()
    {
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        var models = settings.GetFeature<RenderModelFeature>();
        Assert.That(models != null && models.enabled, "XR_FB_render_model (Fieldmate → Configure Project for Quest)");
        var dataSource = settings.GetFeatures().FirstOrDefault(f => f.GetType().Name == "HandTrackingDataSourceFeature");
        Assert.That(dataSource != null && dataSource.enabled, "XR_EXT_hand_tracking_data_source: hands posed around held controllers");
    }

    [Test]
    public void Render_model_structs_match_the_openxr_layout()
    {
        Assert.That(Marshal.SizeOf<RenderModelFeature.PathInfo>(), Is.EqualTo(24));
        Assert.That(Marshal.SizeOf<RenderModelFeature.CapabilitiesRequest>(), Is.EqualTo(24));
        Assert.That(Marshal.SizeOf<RenderModelFeature.Properties>(), Is.EqualTo(112));
        Assert.That(Marshal.SizeOf<RenderModelFeature.LoadInfo>(), Is.EqualTo(24));
        Assert.That(Marshal.SizeOf<RenderModelFeature.Buffer>(), Is.EqualTo(32));
    }

    [Test]
    public void Manifest_asks_for_the_render_model_feature_and_permission()
    {
        // Device log 2026-10-01: without these Quest leaves XR_FB_render_model out of the runtime's extension list.
        var names = new RenderModelManifest().ProvideManifestRequirement().OverrideElements
            .Select(e => e.Attributes["name"]).ToList();
        Assert.That(names, Does.Contain(RenderModelManifest.Feature));
        Assert.That(names, Does.Contain(RenderModelManifest.Permission));
    }

    [Test]
    public void Controller_fit_is_saved_and_restored()
    {
        var before = ControllerModel.Fit;
        try
        {
            ControllerModel.Fit = new Vector4(0.004f, -0.012f, 0.031f, -7.5f);
            ControllerModel.SaveFit();
            ControllerModel.Fit = Vector4.zero;
            Assert.That(ControllerModel.LoadFit(), Is.EqualTo(new Vector4(0.004f, -0.012f, 0.031f, -7.5f)));
            Assert.That(ControllerFit.Describe(ControllerModel.LoadFit()), Is.EqualTo("x 0.004 y -0.012 z 0.031 m, pitch -7.5°"));
        }
        finally
        {
            ControllerModel.Fit = before;
            ControllerModel.SaveFit();
        }
    }
}
