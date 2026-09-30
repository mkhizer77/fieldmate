using System.IO;
using TMPro;
using TMPro.EditorUtilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Fieldmate.Editor;

/// <summary>
/// UI groundwork (#53): imports the TextMeshPro essentials (shaders, TMP Settings) and builds the Inter SDF font assets
/// the runtime UI kit loads from Resources. Idempotent; run from the menu or in batch mode
/// (<c>-executeMethod Fieldmate.Editor.UiSetup.Build</c>). The fonts are OFL (see Assets/_Project/Fonts/Inter-LICENSE.txt).
/// </summary>
public static class UiSetup
{
    private const string FontsDir = "Assets/_Project/Fonts";
    private const string ResourcesDir = "Assets/_Project/Resources/Fonts";

    // ASCII, Latin-1 (German umlauts and ß) and the few symbols the UI uses. Baked so nothing is rasterised on device.
    private const string Characters = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~"
                                      + "ÄÖÜäöüßÀÂÇÉÈÊËÎÏÔŒÙÛÜŸàâçéèêëîïôœùûÿ°±×÷" + "–—‘’“”…·•→←↑↓✓✗⚙";

    [MenuItem("Fieldmate/Build UI Fonts")]
    public static void Build()
    {
        if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0)
        {
            AssetDatabase.ImportPackage(TMP_EditorUtility.packageFullPath + "/Package Resources/TMP Essential Resources.unitypackage", false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        ProjectSetup.EnsureFolder(ResourcesDir);
        BuildFont("Inter-Regular", "InterRegular SDF");
        BuildFont("Inter-SemiBold", "InterSemiBold SDF");
        AssetDatabase.SaveAssets();
        Debug.Log("[UiSetup] TMP essentials imported, Inter SDF font assets built.");
    }

    private static void BuildFont(string ttfName, string assetName)
    {
        var path = $"{ResourcesDir}/{assetName}.asset";
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null)
        {
            return;
        }

        var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontsDir}/{ttfName}.ttf")
            ?? throw new FileNotFoundException($"{FontsDir}/{ttfName}.ttf");
        var asset = TMP_FontAsset.CreateFontAsset(font, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
        asset.name = assetName;
        asset.TryAddCharacters(Characters, out var missing);
        if (!string.IsNullOrEmpty(missing))
        {
            Debug.LogWarning($"[UiSetup] {ttfName} lacks: {missing}");
        }

        asset.material.name = $"{assetName} Material";
        asset.atlasTexture.name = $"{assetName} Atlas";
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
        // Static after baking: the atlas is final and the source font is never read on device. Anything outside the baked
        // set (an emoji in a model reply) falls back to TMP's dynamic LiberationSans.
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset");
        if (fallback != null)
        {
            asset.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
        }

        EditorUtility.SetDirty(asset);
        AssetDatabase.ImportAsset(path);
    }
}
