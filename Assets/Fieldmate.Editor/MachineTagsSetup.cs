using System.IO;
using Fieldmate.Knowledge;
using Fieldmate.Markers;
using Fieldmate.Twin;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Fieldmate.Editor;

/// <summary>
/// Machine tags (#27): the pump skid's <see cref="MachineDefinition"/>, its Addressables entry (address
/// <c>machines/tag-07</c>) and the printable tag in docs/markers. Run by the bench builder; idempotent.
/// </summary>
public static class MachineTagsSetup
{
    public const int PumpSkidTag = 7;
    public const string DefinitionPath = "Assets/_Project/Machines/FM200.asset";
    public const string GroupName = "Machines";
    public const string Label = "machine";
    private const string ManualPath = "Assets/_Project/Manual/manual.json";

    /// <summary>The skid stands behind the tag: the tag lies on the floor at its front edge, read from the front.</summary>
    public static readonly Vector3 SkidFromTag = new(0f, 0f, -0.9f);

    [MenuItem("Fieldmate/Set Up Machine Tags")]
    public static void Ensure()
    {
        ProjectSetup.EnsureFolder("Assets/_Project/Machines");
        var manual = AssetDatabase.LoadAssetAtPath<TextAsset>(ManualPath);
        var definition = AssetDatabase.LoadAssetAtPath<MachineDefinition>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<MachineDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }

        definition.Configure(PumpSkidTag, MachineManual.Parse(manual.text).MachineName, "FM-200 pump skid", manual, 0.2f,
            TagMountKind.Floor, SkidFromTag, 0f);
        EditorUtility.SetDirty(definition);

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        var group = settings.FindGroup(GroupName) ??
                    settings.CreateGroup(GroupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(DefinitionPath), group);
        entry.address = MachineDefinition.AddressFor(PumpSkidTag);
        settings.AddLabel(Label);
        entry.SetLabel(Label, true);
        AssetDatabase.SaveAssets();
        ExportTag(PumpSkidTag);
    }

    /// <summary>docs/markers/tag-NN.png: 100 px per cell, print the black square at 20 cm.</summary>
    public static string ExportTag(int id)
    {
        var gray = MarkerImage.Render(id, 100, out var size);
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var g = gray[y * size + x];
                pixels[(size - 1 - y) * size + x] = new Color32(g, g, g, 255); // textures are stored bottom row first
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        var path = Path.Combine(Directory.GetCurrentDirectory(), "docs", "markers", $"tag-{id:00}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        return path;
    }
}
