using System.Collections;
using System.Linq;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.UI;

/// <summary>#18: the Stats button shows the overlay with live numbers; hidden by default; a log line every 5 s.</summary>
public class PerfOverlayPlayModeTests
{
    [UnityTest]
    public IEnumerator Stats_button_toggles_the_overlay()
    {
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/AssistantBench.unity", LoadSceneMode.Single);
        yield return null;
        var overlay = Object.FindAnyObjectByType<PerfOverlay>();
        Assert.That(overlay, Is.Not.Null);
        Assert.That(overlay.IsShown, Is.False, "hidden by default: this is a product, not a profiler");
        var button = Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == "Stats Button");
        Assert.That(button.Label, Is.EqualTo("Stats"));

        button.Press();
        yield return new WaitForSeconds(0.7f);
        Assert.That(overlay.IsShown, Is.True);
        Assert.That(button.Label, Is.EqualTo("Stats: On"));
        Assert.That(overlay.Text, Does.Contain("fps").And.Contain("Draws").And.Contain("Mem"));
        var sample = overlay.Sample();
        Assert.That(sample.SystemMemoryBytes, Is.GreaterThan(0), "memory counters are live");
        Debug.Log($"[Test] editor render counters: draws {sample.DrawCalls} tris {sample.Triangles} (0 in a headless batch run; live on device)");

        yield return new WaitForSeconds(0.7f); // past the press debounce
        button.Press();
        yield return null;
        Assert.That(overlay.IsShown, Is.False);
    }
}
