using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fieldmate.Editor;

/// <summary>Batch-mode build entry points used by tools/build.sh and CI.</summary>
public static class BuildScript
{
    private const string PackageName = "com.khizerkhalid.fieldmate";

    public static void BuildAndroid()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("No scenes enabled in Build Settings.");
        }

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        EditorUserBuildSettings.buildAppBundle = false;

        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
        Directory.CreateDirectory(outputDir);
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outputDir, "Fieldmate.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };

        Run(options);
    }

    /// <summary>The remote observer for macOS (#26): Builds/FieldmateObserver.app, the observer scene only.</summary>
    public static void BuildObserverMac()
    {
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, PackageName + ".observer");
        // Shared code references Microphone (the headset's voice loop), so macOS insists on a reason; the observer never records.
        PlayerSettings.macOS.microphoneUsageDescription = "Fieldmate Observer does not record audio.";
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
        Directory.CreateDirectory(outputDir);
        Run(new BuildPlayerOptions
        {
            scenes = new[] { ObserverSceneBuilder.ScenePath },
            locationPathName = Path.Combine(outputDir, "FieldmateObserver.app"),
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        });
    }

    private static void Run(BuildPlayerOptions options)
    {
        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log($"Build {summary.result}: {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime}");
        if (summary.result != BuildResult.Succeeded)
        {
            throw new Exception($"Build failed with {summary.totalErrors} errors.");
        }
    }
}
