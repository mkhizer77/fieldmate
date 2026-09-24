using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Fieldmate.Editor;

/// <summary>
/// Idempotent project configuration for Quest 3: Android/IL2CPP/ARM64/Vulkan, URP, OpenXR with the Meta Quest
/// feature group. Run once in batch mode (<c>-executeMethod Fieldmate.Editor.ProjectSetup.ConfigureQuest</c>) or
/// from the Fieldmate menu; safe to re-run.
/// </summary>
public static class ProjectSetup
{
    private const string SettingsDir = "Assets/_Project/Settings";
    private const string MetaFeatureSetId = "com.unity.openxr.featureset.meta";

    // Matched by type name so this script does not need a compile-time dependency on every feature assembly.
    private static readonly HashSet<string> AndroidFeatures = new()
    {
        "MetaQuestFeature",
        "ARSessionFeature", "ARCameraFeature", "ARPlaneFeature", "ARAnchorFeature",
        "AROcclusionFeature", "ARMeshFeature", "ARRaycastFeature", "DisplayUtilitiesFeature",
        "HandTracking", "MetaHandTrackingAim",
        "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile", "HandInteractionProfile",
    };

    [MenuItem("Fieldmate/Configure Project for Quest")]
    public static void ConfigureQuest()
    {
        ConfigurePlayer();
        ConfigureUrp();
        ConfigureXr();
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectSetup] Quest configuration applied.");
    }

    private static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "Khizer Khalid";
        PlayerSettings.productName = "Fieldmate";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.khizerkhalid.fieldmate");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.colorSpace = ColorSpace.Linear;

        // Input System only (XRI 3.x). No public API for this; 1 = new Input System.
        var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (playerSettings != null)
        {
            var so = new SerializedObject(playerSettings);
            var handler = so.FindProperty("activeInputHandler");
            if (handler != null)
            {
                handler.intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static void ConfigureUrp()
    {
        EnsureFolder(SettingsDir);
        var rendererPath = $"{SettingsDir}/Quest_Renderer.asset";
        var assetPath = $"{SettingsDir}/Quest_URP.asset";

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, rendererPath);
        }

        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        if (urp == null)
        {
            urp = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(urp, assetPath);
        }

        // Quest defaults (design.md §6): 4x MSAA, no HDR, no depth/opaque textures.
        urp.msaaSampleCount = 4;
        urp.supportsHDR = false;
        urp.supportsCameraDepthTexture = false;
        urp.supportsCameraOpaqueTexture = false;
        EditorUtility.SetDirty(urp);

        GraphicsSettings.defaultRenderPipeline = urp;
        var current = QualitySettings.GetQualityLevel();
        for (var i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(current, false);
    }

    private static void ConfigureXr()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
        {
            EnsureFolder("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }

        const BuildTargetGroup android = BuildTargetGroup.Android;
        if (!perTarget.HasSettingsForBuildTarget(android))
        {
            perTarget.CreateDefaultSettingsForBuildTarget(android);
        }
        if (!perTarget.HasManagerSettingsForBuildTarget(android))
        {
            perTarget.CreateDefaultManagerSettingsForBuildTarget(android);
        }

        var general = perTarget.SettingsForBuildTarget(android);
        general.InitManagerOnStart = true;
        XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, android);

        FeatureHelpers.RefreshFeatures(android);
        var featureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(android, MetaFeatureSetId);
        if (featureSet != null)
        {
            featureSet.isEnabled = true;
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(android);
        }
        else
        {
            Debug.LogWarning($"[ProjectSetup] Feature set {MetaFeatureSetId} not found.");
        }

        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(android);
        var enabled = new List<string>();
        foreach (var feature in settings.GetFeatures())
        {
            if (AndroidFeatures.Contains(feature.GetType().Name))
            {
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }
            if (feature.enabled)
            {
                enabled.Add(feature.GetType().Name);
            }
        }
        EditorUtility.SetDirty(settings);
        Debug.Log($"[ProjectSetup] OpenXR Android features enabled: {string.Join(", ", enabled.OrderBy(n => n))}");

        var missing = AndroidFeatures.Except(enabled).ToList();
        if (missing.Count > 0)
        {
            Debug.LogWarning($"[ProjectSetup] Features not found: {string.Join(", ", missing)}");
        }
    }

    // Creates folders through the AssetDatabase; creating them on disk first makes packages that also call
    // AssetDatabase.CreateFolder produce duplicates ("XR 1").
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }
        var parent = path[..path.LastIndexOf('/')];
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path[(path.LastIndexOf('/') + 1)..]);
    }
}
