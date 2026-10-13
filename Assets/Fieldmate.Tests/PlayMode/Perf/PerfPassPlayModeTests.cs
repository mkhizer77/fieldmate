using System.Collections;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Perf;

/// <summary>#29: hidden UI costs nothing, and the headset settings component is in the bench.</summary>
public class PerfPassPlayModeTests
{
    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Hidden_overlays_switch_their_canvas_off_not_just_their_alpha()
    {
        var overlay = Object.FindAnyObjectByType<PerfOverlay>();
        var overlayCanvas = overlay.GetComponent<Canvas>();
        Assert.That(overlay.IsShown, Is.False);
        Assert.That(overlayCanvas.enabled, Is.False, "a canvas at alpha 0 still draws");
        overlay.Toggle();
        Assert.That(overlayCanvas.enabled, Is.True);
        overlay.Toggle();
        Assert.That(overlayCanvas.enabled, Is.False);

        var ring = Object.FindAnyObjectByType<GazeRing>(FindObjectsInactive.Include);
        var ringCanvas = ring.GetComponent<Canvas>();
        Assert.That(ringCanvas.enabled, Is.False);
        ring.Show(Vector3.forward, 0.5f);
        Assert.That(ringCanvas.enabled, Is.True);
        ring.Hide();
        Assert.That(ringCanvas.enabled, Is.False);
        yield break;
    }

    [UnityTest]
    public IEnumerator Quest_performance_waits_for_a_headset_display()
    {
        var performance = Object.FindAnyObjectByType<QuestPerformance>();
        Assert.That(performance, Is.Not.Null);
        Assert.That(performance.Foveation, Is.GreaterThan(0f));
        yield return new WaitForSecondsRealtime(1.2f);
        Assert.That(performance.Applied, Is.False, "the editor has no running XR display");
    }
}
