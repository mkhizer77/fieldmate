using System.Collections;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Procedures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.UI;

/// <summary>#53: the runtime behaviour of the polished UI — progress, status fade, button press, tag fade, state pulse.</summary>
public class UiPolishPlayModeTests
{
    private readonly System.Collections.Generic.List<GameObject> spawned = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in spawned)
        {
            if (go != null) Object.Destroy(go);
        }

        spawned.Clear();
    }

    private T Spawn<T>(string name) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        spawned.Add(go);
        return go.AddComponent<T>();
    }

    [UnityTest]
    public IEnumerator Step_card_shows_progress_and_fades_its_status()
    {
        var panel = Spawn<ProcedurePanel>("Panel");
        yield return null;
        panel.ShowStep(3, 8, "Close the inlet", "Pinch the lever.", "Relief valve replacement");
        Assert.That(panel.ProgressTotal, Is.EqualTo(8));
        Assert.That(panel.ProgressDone, Is.EqualTo(2));
        Assert.That(panel.ChipText, Is.EqualTo("Step 3 of 8"));
        Assert.That(panel.TitleText, Is.EqualTo("Step 3 of 8: Close the inlet"));

        panel.ShowStatus("✓ Close the inlet", ProcedurePanel.Done, 0.2f);
        Assert.That(panel.StatusText, Is.EqualTo("✓ Close the inlet"));
        yield return new WaitForSeconds(0.8f);
        Assert.That(panel.StatusText, Is.Empty, "status lines fade out after their time");

        panel.ShowIdle("Step 0: Place the machine", "…", "Setup");
        Assert.That(panel.ProgressTotal, Is.EqualTo(0));
        Assert.That(panel.ChipText, Is.EqualTo("Setup"));

        // Every image and label of the card draws on top of the machine.
        foreach (var image in panel.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            Assert.That(image.material.shader.name, Is.EqualTo(Fieldmate.UI.UiKit.ImageOverlayShader), image.name);
        }

        foreach (var text in panel.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            Assert.That(text.fontSharedMaterial.shader.name, Is.EqualTo(Fieldmate.UI.UiKit.TextOverlayShader), text.name);
        }
    }

    [UnityTest]
    public IEnumerator Button_cap_depresses_on_press_and_returns()
    {
        var go = new GameObject("Button");
        spawned.Add(go);
        var button = go.AddComponent<PressButton>();
        yield return null;
        Assert.That(go.GetComponentInChildren<BoxCollider>(), Is.Not.Null, "the cap is the collider");
        Assert.That(button.Label, Is.EqualTo("Start"));
        button.SetStyle(ButtonStyle.Primary);
        Assert.That(button.Style, Is.EqualTo(ButtonStyle.Primary));

        var pressed = 0;
        button.Pressed += () => pressed++;
        button.Press();
        yield return null;
        yield return null;
        Assert.That(pressed, Is.EqualTo(1));
        Assert.That(button.CapDepth, Is.GreaterThan(0f), "the cap moves in");
        yield return new WaitForSeconds(0.4f);
        Assert.That(button.CapDepth, Is.EqualTo(0f), "and comes back");
    }

    [UnityTest]
    public IEnumerator Control_tag_fades_with_distance()
    {
        var camGo = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
        spawned.Add(camGo);
        var control = new GameObject("Lever");
        spawned.Add(control);
        var tag = control.AddComponent<ControlTag>();
        tag.Configure("Inlet valve", "pump suction", new Vector3(0f, 0.12f, 0f));
        tag.SetViewer(camGo.GetComponent<Camera>()); // a scene left by another test may keep its own main camera
        camGo.transform.position = new Vector3(0f, 0f, -0.8f);
        yield return null;
        yield return null;
        Assert.That(tag.Alpha, Is.EqualTo(0f), "#73: hidden until a step needs it");
        tag.Relevant = true;
        yield return new WaitForSeconds(0.4f);
        Assert.That(tag.Text, Is.EqualTo("Inlet valve\n<size=26>pump suction</size>"));
        Assert.That(tag.Alpha, Is.EqualTo(1f).Within(0.01f), "fully visible within reach");

        camGo.transform.position = new Vector3(0f, 0f, -6f);
        yield return new WaitForSeconds(0.4f);
        Assert.That(tag.Alpha, Is.EqualTo(0.25f).Within(0.01f), "faded far away, never hidden while needed");
    }

    [UnityTest]
    public IEnumerator Assistant_state_dot_pulses_only_while_busy()
    {
        var panel = Spawn<AssistantPanel>("Assistant Panel");
        yield return null;
        panel.SetState(AssistantState.Idle, "Pinch to talk");
        Assert.That(panel.IsPulsing, Is.False);
        Assert.That(panel.StateLabel, Is.EqualTo("Pinch to talk"));
        panel.SetState(AssistantState.Listening, "Pinch to talk");
        Assert.That(panel.IsPulsing, Is.True);
        Assert.That(panel.StateLabel, Is.EqualTo("Listening…"));
        panel.SetState(AssistantState.Speaking, null);
        Assert.That(panel.IsPulsing, Is.True);
        panel.SetState(AssistantState.Idle, "Pinch to talk");
        Assert.That(panel.IsPulsing, Is.False);
    }

    [UnityTest]
    public IEnumerator Gaze_ring_fills_and_hides()
    {
        var head = new GameObject("Head").transform;
        spawned.Add(head.gameObject);
        var ring = GazeRing.Create(head);
        spawned.Add(ring.gameObject);
        yield return null;
        Assert.That(ring.IsShowing, Is.False);
        head.position = new Vector3(0f, 1f, 0f);
        ring.ShowInFront(new Vector3(0f, 1f, 1f), 0.1f, 0.5f);
        Assert.That(ring.IsShowing, Is.True);
        Assert.That(ring.transform.position.z, Is.EqualTo(1f - 0.1f - 0.06f).Within(1e-3f), "just in front of the part, toward the viewer");
        Assert.That(ring.Progress, Is.EqualTo(0.5f).Within(1e-4f));
        foreach (var image in ring.GetComponentsInChildren<UnityEngine.UI.Image>())
        {
            Assert.That(image.material.shader.name, Is.EqualTo(Fieldmate.UI.UiKit.ImageOverlayShader), "drawn over the machine");
        }

        ring.Hide();
        Assert.That(ring.IsShowing, Is.False);
    }
}
