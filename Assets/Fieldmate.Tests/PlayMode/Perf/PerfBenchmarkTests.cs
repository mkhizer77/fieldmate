using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Tests.PlayMode.Assistant;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Perf;

/// <summary>
/// #29: the bench in its busiest steady state (machine placed, procedure running, every label shown, the user at
/// working distance) measured over 300 frames in the editor. Absolute times are the Mac's, not the Quest's; what
/// carries over is the work per frame: managed allocations, draw calls, SetPass calls, triangles, and our scripts'
/// share of the main thread. Writes TestResults/perf-bench.md for docs/perf/before-after.md.
/// </summary>
public class PerfBenchmarkTests
{
    private const int Warmup = 90;
    private const int Frames = 300;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        yield return ProcedurePlayModeTests.PlaceMachine();
        var machine = Object.FindAnyObjectByType<MachineServices>();
        var skid = machine.Parts["relief_valve"].transform.root;
        var head = Camera.main.transform;
        head.SetPositionAndRotation(skid.position + skid.forward * 1.6f + Vector3.up * 1.6f,
            Quaternion.LookRotation(skid.position + Vector3.up * 0.8f - (skid.position + skid.forward * 1.6f + Vector3.up * 1.6f)));
        machine.Runner.Start(machine.Now);
        ControlTag.ShowAll = true;

        // Batch mode has no game view: render the user's camera into one Quest-eye-sized target each frame so the render
        // counters see a real frame. Multiview draws both eyes in these same calls.
        eye = new RenderTexture(1680, 1760, 24) { antiAliasing = 4 };
        Camera.main.targetTexture = eye;
    }

    private RenderTexture eye;

    [TearDown]
    public void Restore()
    {
        ControlTag.ShowAll = false;
        if (Camera.main != null)
        {
            Camera.main.targetTexture = null;
        }

        eye?.Release();
    }

    private static IEnumerator Frame()
    {
        Camera.main.Render(); // WaitForEndOfFrame never fires in batch mode; render after this frame's updates instead
        yield return null;
    }

    [UnityTest]
    public IEnumerator Steady_state_frame_work()
    {
        for (var i = 0; i < Warmup; i++)
        {
            yield return null;
        }

        using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", Frames);
        using var gcCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count", Frames);
        using var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", Frames);
        using var update = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BehaviourUpdate", Frames);
        using var late = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LateBehaviourUpdate", Frames);
        using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", Frames);
        using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", Frames);
        using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", Frames);
        using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", Frames);
        using var canvas = ProfilerRecorder.StartNew(ProfilerCategory.Gui, "Canvas.SendWillRenderCanvases", Frames);
        for (var i = 0; i < Frames; i++)
        {
            yield return Frame();
        }

        var report = new StringBuilder();
        report.AppendLine("| Metric (editor, per frame) | Median | Max |").AppendLine("|---|---|---|");
        Row(report, "GC allocated (bytes)", gc, 1);
        Row(report, "GC allocations (count)", gcCount, 1);
        Row(report, "Main thread (ms)", main, 1e-6);
        Row(report, "BehaviourUpdate (ms)", update, 1e-6);
        Row(report, "LateBehaviourUpdate (ms)", late, 1e-6);
        Row(report, "Canvas rebuilds (ms)", canvas, 1e-6);
        Row(report, "Draw calls", draws, 1);
        Row(report, "SetPass calls", setPass, 1);
        Row(report, "Batches", batches, 1);
        Row(report, "Triangles", tris, 1);
        Directory.CreateDirectory("TestResults");
        File.WriteAllText("TestResults/perf-bench.md", report.ToString());
        Debug.Log("[Perf] bench\n" + report);

        // The design's rule (§6): nothing allocates per frame in steady state. The editor and the test runner allocate a
        // few hundred bytes every frame of their own, so measure that floor with every Fieldmate behaviour switched off
        // and hold the app to it.
        var withApp = Median(Samples(gc));
        LogAssert.ignoreFailingMessages = true; // AR Foundation complains when its managers are disabled without XR
        var ours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(m => m.enabled && m.GetType().FullName!.StartsWith("Fieldmate.")).ToList();
        foreach (var m in ours)
        {
            m.enabled = false;
        }

        using var floor = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 60);
        for (var i = 0; i < 60; i++)
        {
            yield return Frame();
        }

        var editorFloor = Median(Samples(floor));
        File.AppendAllText("TestResults/perf-bench.md", $"\nEditor floor (all Fieldmate behaviours off): {editorFloor:0} B/frame; the app adds {withApp - editorFloor:0} B/frame.\n");
        Assert.That(withApp - editorFloor, Is.LessThanOrEqualTo(32), $"app allocations per frame: {withApp:0} B with the app, {editorFloor:0} B editor floor");
    }

    private static List<double> Samples(ProfilerRecorder recorder)
    {
        var list = new List<ProfilerRecorderSample>(recorder.Capacity);
        recorder.CopyTo(list);
        return list.Select(s => (double)s.Value).ToList();
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private static void Row(StringBuilder report, string name, ProfilerRecorder recorder, double scale)
    {
        var samples = Samples(recorder);
        var c = CultureInfo.InvariantCulture;
        report.AppendLine(recorder.Valid && samples.Count > 0
            ? string.Format(c, "| {0} | {1:0.###} | {2:0.###} |", name, Median(samples) * scale, samples.Max() * scale)
            : $"| {name} | n/a | n/a |");
    }
}
