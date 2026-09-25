using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fieldmate.Knowledge;
using Fieldmate.Twin;
using UnityEditor;
using UnityEngine;

namespace Fieldmate.Editor;

/// <summary>
/// Checks manual.json for broken references and compares its parts with the <see cref="PartTag"/>s in the open scenes:
/// manual parts with no tagged object, and tags whose id the manual does not know.
/// </summary>
public static class ManualValidator
{
    public const string ManualPath = "Assets/_Project/Manual/manual.json";

    public sealed class Report
    {
        public List<string> ManualProblems { get; } = new();
        public List<string> MissingInScene { get; } = new();
        public List<string> UnknownTags { get; } = new();

        public bool IsClean => ManualProblems.Count == 0 && MissingInScene.Count == 0 && UnknownTags.Count == 0;
    }

    [MenuItem("Fieldmate/Validate Manual Against Open Scenes")]
    public static void ValidateOpenScenes()
    {
        var tags = Object.FindObjectsByType<PartTag>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var report = Validate(File.ReadAllText(ManualPath), tags.Select(t => t.PartId));

        foreach (var problem in report.ManualProblems)
        {
            Debug.LogError($"[ManualValidator] manual.json: {problem}");
        }

        foreach (var part in report.MissingInScene)
        {
            Debug.LogWarning($"[ManualValidator] Part '{part}' is in the manual but no PartTag in the open scenes uses it.");
        }

        foreach (var tag in tags.Where(t => report.UnknownTags.Contains(t.PartId)))
        {
            Debug.LogError($"[ManualValidator] '{tag.name}' has PartTag '{tag.PartId}', which the manual does not define.", tag);
        }

        if (report.IsClean)
        {
            Debug.Log($"[ManualValidator] manual.json and the open scenes agree ({tags.Length} tagged parts).");
        }
    }

    /// <summary>Validates manual JSON against the part ids tagged in a scene.</summary>
    public static Report Validate(string manualJson, IEnumerable<string> scenePartIds)
    {
        var report = new Report();
        MachineManual manual;
        try
        {
            manual = MachineManual.Parse(manualJson);
        }
        catch (System.Exception e) when (e is Json.JsonException or ManualFormatException)
        {
            report.ManualProblems.Add(e.Message);
            return report;
        }

        report.ManualProblems.AddRange(manual.Validate());
        var sceneIds = scenePartIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        report.MissingInScene.AddRange(manual.FindPartsMissingFrom(sceneIds));
        report.UnknownTags.AddRange(sceneIds.Where(id => !manual.TryGetPart(id, out _)));
        return report;
    }
}
