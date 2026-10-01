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
    public IEnumerator EveryControl_HasATagThatFollowsItsState()
    {
        var tags = Object.FindObjectsByType<ControlTag>(FindObjectsSortMode.None);
        Assert.That(tags.Select(t => t.Text.Split('\n')[0]), Is.EquivalentTo(new[]
            { "Inlet valve", "Outlet valve", "Main breaker", "Pump cover", "New relief cartridge" }));

        var lever = Object.FindObjectsByType<RotaryInteractable>(FindObjectsSortMode.None).Single(r => r.PartId == "inlet_valve");
        lever.SetAngle(90f);
        yield return null;
        Assert.That(lever.GetComponent<ControlTag>().Text, Does.Contain("CLOSED"));
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
        highlighter.Highlight(breaker, "Main breaker", "turn to LOCKED", 30f);
        yield return null;
        yield return null;
        Assert.That(Vector3.Distance(highlighter.Marker.position, tag.LabelPosition), Is.LessThan(0.001f));
        Assert.That(tag.Alpha, Is.EqualTo(0f), "one label, not two");
        highlighter.Clear();
        yield return null;
        yield return null;
        Assert.That(tag.Alpha, Is.GreaterThan(0f));
    }
}
