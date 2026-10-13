using System.Linq;
using Fieldmate.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>
/// #29: the settings the performance pass depends on stay set. Each one was off or wrong before and costs frame time
/// on the headset if it regresses; ProjectSetup.ConfigureQuest applies them.
/// </summary>
public class QuestPerfSettingsTests
{
    private const string UrpPath = "Assets/_Project/Settings/Quest_URP.asset";
    private const string RendererPath = "Assets/_Project/Settings/Quest_Renderer.asset";

    private static OpenXRSettings Android => OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);

    [Test]
    public void Quest_renders_both_eyes_in_one_pass_with_fixed_foveation_and_level_hints()
    {
        Assert.That(Android.renderMode, Is.EqualTo(OpenXRSettings.RenderMode.SinglePassInstanced), "multiview");
        Assert.That(Android.GetFeature<FoveatedRenderingFeature>()?.enabled, Is.True, "foveated rendering");
        Assert.That(Android.GetFeature<XrPerformanceSettingsFeature>()?.enabled, Is.True, "CPU/GPU level hints");
    }

    [Test]
    public void Player_settings_leave_frame_pacing_to_the_compositor_and_logs_cheap()
    {
        Assert.That(PlayerSettings.Android.optimizedFramePacing, Is.False, "Swappy fights the XR compositor's pacing");
        Assert.That(PlayerSettings.GetStackTraceLogType(LogType.Log), Is.EqualTo(StackTraceLogType.None));
        Assert.That(PlayerSettings.GetStackTraceLogType(LogType.Warning), Is.EqualTo(StackTraceLogType.None));
        Assert.That(PlayerSettings.GetStackTraceLogType(LogType.Exception), Is.Not.EqualTo(StackTraceLogType.None), "errors keep theirs");

        var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").First());
        Assert.That(so.FindProperty("meshDeformation").intValue, Is.EqualTo(2), "hand meshes skinned on the GPU");
    }

    [Test]
    public void Urp_has_nothing_the_frame_does_not_use()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
        Assert.That(urp.supportsHDR, Is.False);
        Assert.That(urp.supportsCameraDepthTexture, Is.False);
        Assert.That(urp.supportsCameraOpaqueTexture, Is.False);
        Assert.That(urp.msaaSampleCount, Is.EqualTo(4));
        Assert.That(urp.useSRPBatcher, Is.True);
        Assert.That(new SerializedObject(urp).FindProperty("m_MainLightShadowsSupported").boolValue, Is.False, "nothing casts shadows");
    }

    [Test]
    public void The_hologram_depth_pass_runs_once_for_all_renderers_from_a_renderer_feature()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var feature = renderer.rendererFeatures.OfType<RenderObjects>().SingleOrDefault(f => f.name == ProjectSetup.HologramDepthFeature);
        Assert.That(feature, Is.Not.Null);
        Assert.That(feature.isActive, Is.True);
        Assert.That(feature.settings.Event, Is.EqualTo(RenderPassEvent.BeforeRenderingTransparents));
        Assert.That(feature.settings.filterSettings.PassNames, Is.EqualTo(new[] { ProjectSetup.HologramDepthLightMode }));

        var shader = Shader.Find("Fieldmate/Hologram");
        var lightMode = new ShaderTagId("LightMode");
        Assert.That(shader.FindPassTagValue(0, lightMode).name, Is.EqualTo(ProjectSetup.HologramDepthLightMode),
            "a depth pass tagged SRPDefaultUnlit would run per renderer again, alternating with the light pass");
        Assert.That(shader.FindPassTagValue(1, lightMode).name, Is.EqualTo("UniversalForward"));
    }
}
