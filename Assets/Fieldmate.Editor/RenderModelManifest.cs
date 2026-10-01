using System;
using System.Collections.Generic;
using Fieldmate.XR;
using Unity.XR.Management.AndroidManifest.Editor;
using UnityEditor;
using UnityEngine.XR.OpenXR;

namespace Fieldmate.Editor;

/// <summary>
/// The manifest entries Quest needs before it offers XR_FB_render_model to an app (#80): without the RENDER_MODEL
/// feature and permission the runtime leaves the extension out of its list (device log 2026-10-01: 87 extensions, no
/// render model) and the controller models never load. Added only while <see cref="RenderModelFeature"/> is enabled,
/// the same way Unity's Meta package adds passthrough's entries.
/// </summary>
public sealed class RenderModelManifest : IAndroidManifestRequirementProvider
{
    public const string Feature = "com.oculus.feature.RENDER_MODEL";
    public const string Permission = "com.oculus.permission.RENDER_MODEL";

    public int callbackOrder => 3;

    public ManifestRequirement ProvideManifestRequirement()
    {
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        var feature = settings != null ? settings.GetFeature<RenderModelFeature>() : null;
        var elements = new List<ManifestElement>();
        if (feature != null && feature.enabled)
        {
            elements.Add(new ManifestElement
            {
                ElementPath = new List<string> { "manifest", "uses-feature" },
                Attributes = new Dictionary<string, string> { { "name", Feature }, { "required", "false" } },
            });
            elements.Add(new ManifestElement
            {
                ElementPath = new List<string> { "manifest", "uses-permission" },
                Attributes = new Dictionary<string, string> { { "name", Permission } },
            });
        }

        return new ManifestRequirement
        {
            SupportedXRLoaders = new HashSet<Type> { typeof(OpenXRLoader) },
            OverrideElements = elements,
        };
    }
}
