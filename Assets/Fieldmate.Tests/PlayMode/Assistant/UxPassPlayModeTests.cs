using System.Collections;
using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#51: placement first, discoverable controls, input-dependent wording, a panel that stays put.</summary>
public class UxPassPlayModeTests
{
    private MachineServices machine;
    private ProcedurePanel stepPanel;
    private PressButton start;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        InputModalityProbe.Set(Modality.Hands);
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        machine = Object.FindAnyObjectByType<MachineServices>();
        stepPanel = Object.FindAnyObjectByType<ProcedurePanel>();
        start = Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == "Start Button");
    }

    [TearDown]
    public void ResetModality() => InputModalityProbe.Set(Modality.Hands);

    [UnityTest]
    public IEnumerator PlacementIsStepZero_StartIsLockedUntilTheMachineIsPlaced()
    {
        Assert.That(stepPanel.TitleText, Does.StartWith("Step 0: Place the machine"));
        start.Press();
        yield return null;
        Assert.That(machine.Runner.State, Is.Not.EqualTo(Fieldmate.Procedures.RunnerState.Running), "can't start before placing");

        yield return ProcedurePlayModeTests.PlaceMachine();
        Assert.That(start.Label, Is.EqualTo("Start"));
        yield return new WaitForSecondsRealtime(0.7f);
        start.Press();
        yield return null;
        Assert.That(machine.Runner.State, Is.EqualTo(Fieldmate.Procedures.RunnerState.Running));
    }

    [UnityTest]
    public IEnumerator EveryPart_HasOneLabel_HiddenUntilNeeded_AndControlsFollowTheirState()
    {
        var tags = Object.FindObjectsByType<ControlTag>(FindObjectsSortMode.None);
        var parts = Object.FindObjectsByType<Fieldmate.Twin.PartTag>(FindObjectsSortMode.None);
        foreach (var part in parts)
        {
            Assert.That(part.GetComponentsInChildren<ControlTag>(), Has.Length.EqualTo(1), $"#73: {part.PartId} has one label");
        }

        Assert.That(tags.Select(t => t.Text.Split('\n')[0]), Does.Contain("Main breaker").And.Contain("New relief cartridge"));
        yield return new WaitForSeconds(0.4f);
        Assert.That(tags.All(t => t.Alpha == 0f), Is.True, "#73: all labels off by default");

        ControlTag.ShowAll = true;
        yield return new WaitForSeconds(0.4f);
        Assert.That(tags.All(t => t.Alpha > 0f), Is.True, "asked for: every label shows");
        ControlTag.ShowAll = false;

        var lever = Object.FindObjectsByType<RotaryInteractable>(FindObjectsSortMode.None).Single(r => r.PartId == "inlet_valve");
        lever.SetAngle(90f);
        yield return null;
        Assert.That(lever.GetComponent<ControlTag>().Text, Does.Contain("CLOSED"));
    }

    [UnityTest]
    public IEnumerator StepLabels_ShowForTheToolStep_AndGoOnceTheCartridgeIsSeated()
    {
        yield return ProcedurePlayModeTests.PlaceMachine();
        var runner = machine.Runner;
        runner.Start(machine.Now);
        runner.Handle(InteractionEvent.Gaze(machine.Now, "relief_valve", 2f));
        runner.Handle(InteractionEvent.State(machine.Now, "main_breaker", "locked"));
        runner.Handle(InteractionEvent.State(machine.Now, "inlet_valve", "closed"));
        runner.Handle(InteractionEvent.Measured(machine.Now, 0f));
        runner.Handle(InteractionEvent.State(machine.Now, "pump_cover", "removed"));
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("replace"));
        var cartridge = Object.FindAnyObjectByType<ToolItem>().GetComponent<ControlTag>();
        yield return new WaitForSeconds(0.4f);
        Assert.That(cartridge.Relevant, Is.True);
        Assert.That(cartridge.Alpha, Is.GreaterThan(0f), "the tool the step needs is labelled");

        runner.Handle(InteractionEvent.Socketed(machine.Now, "relief_valve_seat", "relief_cartridge"));
        yield return new WaitForSeconds(0.4f);
        Assert.That(cartridge.Alpha, Is.EqualTo(0f), "#73 device test: gone once it is seated");
    }

    [UnityTest]
    public IEnumerator LockoutStep_ShowsTheTurnGuide_AndSaysWhatToDoWithBothHands()
    {
        yield return ProcedurePlayModeTests.PlaceMachine();
        start.Press();
        machine.Runner.Handle(Fieldmate.Procedures.InteractionEvent.Gaze(machine.Now, "relief_valve", 2f));
        yield return null;
        yield return null;

        var guide = Object.FindAnyObjectByType<ControlGuide>();
        Assert.That(machine.Runner.CurrentStep.Id, Is.EqualTo("lockout"));
        Assert.That(guide.IsShowing, Is.True);
        Assert.That(guide.PointCount, Is.GreaterThan(10), "an arc with an arrowhead");
        Assert.That(stepPanel.BodyText, Does.Contain("both hands"));
        Assert.That(Object.FindAnyObjectByType<PartHighlighter>().MarkerLabel, Does.Contain("turn to LOCKED"));
    }

    [UnityTest]
    public IEnumerator Controllers_ChangeTheWording()
    {
        yield return ProcedurePlayModeTests.PlaceMachine();
        start.Press();
        machine.Runner.Handle(Fieldmate.Procedures.InteractionEvent.Gaze(machine.Now, "relief_valve", 2f));
        yield return null;
        Assert.That(stepPanel.BodyText, Does.StartWith("Pinch the red breaker bar"));

        InputModalityProbe.Set(Modality.Controllers);
        yield return null;
        Assert.That(stepPanel.BodyText, Does.StartWith("Grip the red breaker bar with both controllers"));
    }

    [UnityTest]
    public IEnumerator Caption_StaysBesideTheMate_NotWithTheHead_WhenTheHeadMoves()
    {
        var panel = Object.FindAnyObjectByType<AssistantPanel>().transform;
        var mate = Object.FindAnyObjectByType<HologramMate>();
        var head = Camera.main.transform;
        yield return null;
        var before = panel.position;

        head.position += new Vector3(0.6f, 0f, -0.5f);
        head.Rotate(0f, 60f, 0f);
        for (var i = 0; i < 30; i++) yield return null;

        Assert.That(Vector3.Distance(panel.position, mate.HeadPivot.position), Is.InRange(0.12f, 0.3f), "#71: it stays with the mate");
        Assert.That(Vector3.Distance(panel.position, before), Is.LessThan(0.3f), "and only swings round it, it doesn't follow the head");
    }

    [UnityTest]
    public IEnumerator HologramMate_StandsInFrontOfTheUser_WithItsCaption_AndFollowsTheAssistantState()
    {
        var panel = Object.FindAnyObjectByType<AssistantPanel>();
        var mate = Object.FindAnyObjectByType<HologramMate>();
        Assert.That(panel.Mate, Is.SameAs(mate), "#69: the caption drives the figure");
        yield return null;
        yield return null;

        var head = Camera.main.transform;
        var toMate = mate.transform.position - head.position;
        toMate.y = 0f;
        Assert.That(toMate.magnitude, Is.InRange(0.8f, 1.5f), "#71: within a step or two of the user");
        Assert.That(Vector3.Dot(head.forward, toMate.normalized), Is.GreaterThan(0.5f), "in view");
        Assert.That(mate.HeadPivot.position.y, Is.LessThan(head.position.y), "its face a little below the user's eyes");

        var side = panel.transform.position - mate.HeadPivot.position;
        side.y = 0f;
        Assert.That(side.magnitude, Is.InRange(0.12f, 0.3f), "the bubble stands beside the figure, not over it");

        panel.SetState(Fieldmate.AI.AssistantState.Listening, "Pinch to talk");
        Assert.That(mate.State, Is.EqualTo(Fieldmate.AI.AssistantState.Listening));
        panel.SetState(Fieldmate.AI.AssistantState.Idle, "Pinch to talk");
        Assert.That(mate.State, Is.EqualTo(Fieldmate.AI.AssistantState.Idle));
    }

    [UnityTest]
    public IEnumerator Highlight_RingHugsThePart_AndTheCalloutIsTheTagStyle_InTheTagsPlace()
    {
        yield return ProcedurePlayModeTests.PlaceMachine();
        var highlighter = Object.FindAnyObjectByType<PartHighlighter>();
        Assert.That(machine.TryGetPart("relief_valve", out var valve), Is.True);
        highlighter.Highlight(valve, "Relief valve");
        yield return null;
        yield return null;
        var bounds = valve.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        Assert.That(highlighter.RingDiameter, Is.LessThan(bounds.extents.magnitude * 2f), "#71: tighter than the old 3D-diagonal ring");
        Assert.That(highlighter.RingDiameter, Is.GreaterThan(Mathf.Max(bounds.size.x, bounds.size.y) * 0.8f), "but still around the part");

        var texts = highlighter.Marker.GetComponentsInChildren<TMPro.TMP_Text>(true);
        Assert.That(texts.Single().color, Is.EqualTo(Fieldmate.UI.Theme.TextPrimary), "#71: the same text style as a control's tag");

        // A control with its own tag: the callout takes the tag's place, and the tag steps aside until cleared.
        Assert.That(machine.TryGetPart("main_breaker", out var breaker), Is.True);
        var tag = breaker.GetComponentInChildren<ControlTag>();
        tag.Relevant = true;
        highlighter.Highlight(breaker, "Main breaker", "turn to LOCKED", 30f);
        yield return null;
        yield return null;
        Assert.That(Vector3.Distance(highlighter.Marker.position, tag.LabelPosition), Is.LessThan(0.001f));
        Assert.That(tag.Alpha, Is.EqualTo(0f), "one label, not two");
        highlighter.Clear();
        yield return new WaitForSeconds(0.4f);
        Assert.That(tag.Alpha, Is.GreaterThan(0f));
    }
}
