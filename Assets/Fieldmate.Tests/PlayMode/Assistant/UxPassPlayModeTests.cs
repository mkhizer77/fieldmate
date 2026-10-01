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
    public IEnumerator AssistantPanel_StaysBesideTheMachine_WhenTheHeadMoves()
    {
        var panel = Object.FindAnyObjectByType<AssistantPanel>().transform;
        var head = Camera.main.transform;
        yield return null;
        var before = panel.position;

        head.position += new Vector3(1f, 0f, -0.5f);
        head.Rotate(0f, 60f, 0f);
        for (var i = 0; i < 30; i++) yield return null;

        Assert.That(Vector3.Distance(panel.position, before), Is.LessThan(0.001f), "no head follow");
    }

    [UnityTest]
    public IEnumerator HologramMate_StandsBesideTheMachine_WithItsCaption_AndFollowsTheAssistantState()
    {
        var panel = Object.FindAnyObjectByType<AssistantPanel>();
        var mate = Object.FindAnyObjectByType<HologramMate>();
        Assert.That(panel.Mate, Is.SameAs(mate), "#69: the caption drives the figure");
        yield return null;

        var skid = Object.FindAnyObjectByType<RemovablePart>().transform.root;
        var machine = skid.GetComponentsInChildren<MeshRenderer>().Where(r => r.GetComponentInParent<Canvas>() == null)
            .Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        var foot = mate.transform.position;
        Assert.That(foot.y, Is.EqualTo(skid.position.y).Within(0.001f), "the pedestal stands on the machine's floor");
        var clearance = new Vector2(Mathf.Max(machine.min.x - foot.x, foot.x - machine.max.x), Mathf.Max(machine.min.z - foot.z, foot.z - machine.max.z));
        Assert.That(Mathf.Max(clearance.x, clearance.y), Is.GreaterThan(0.2f), "pedestal clear of the machine");

        var side = panel.transform.position - mate.HeadPivot.position;
        side.y = 0f;
        Assert.That(side.magnitude, Is.GreaterThan(0.3f), "the caption stands beside the figure, not over it");

        panel.SetState(Fieldmate.AI.AssistantState.Listening, "Pinch to talk");
        Assert.That(mate.State, Is.EqualTo(Fieldmate.AI.AssistantState.Listening));
        panel.SetState(Fieldmate.AI.AssistantState.Idle, "Pinch to talk");
        Assert.That(mate.State, Is.EqualTo(Fieldmate.AI.AssistantState.Idle));
    }
}
