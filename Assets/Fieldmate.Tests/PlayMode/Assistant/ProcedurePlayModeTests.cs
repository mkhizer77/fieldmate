using System.Collections;
using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>The whole relief-valve procedure in the bench scene, done with the real controls, gaze and gauge (#12).</summary>
public class ProcedurePlayModeTests
{
    private MachineServices machine;
    private ScriptedHands hands;
    private Transform head;
    private ProcedurePanel panel;
    private ProcedureResult result;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        machine = Object.FindAnyObjectByType<MachineServices>();
        hands = new ScriptedHands(Object.FindAnyObjectByType<XRInteractionManager>());
        head = Camera.main.transform;
        panel = Object.FindAnyObjectByType<ProcedurePanel>();
        machine.Runner.ProcedureCompleted += r => result = r;
        Time.timeScale = 4f; // telemetry settles in seconds; keep the test short
    }

    [TearDown]
    public void RestoreTime() => Time.timeScale = 1f;

    private static PressButton StartButton() =>
        Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == "Start Button");

    private static RotaryInteractable Rotary(string partId) =>
        Object.FindObjectsByType<RotaryInteractable>(FindObjectsSortMode.None).Single(r => r.PartId == partId);

    private IEnumerator LookAt(string partId, float seconds)
    {
        machine.TryGetPart(partId, out var part);
        var bounds = part.GetComponentsInChildren<Renderer>().Select(r => r.bounds)
            .Aggregate((a, b) => { a.Encapsulate(b); return a; });
        var front = part.transform.root.forward;
        head.position = bounds.center + front * 0.7f;
        head.LookAt(bounds.center);
        yield return new WaitForSeconds(seconds);
    }

    private IEnumerator Turn(RotaryInteractable control, Vector3 axis, float from, float to, bool twoHands)
    {
        var radius = axis == Vector3.up || axis == Vector3.down ? new Vector3(0.08f, 0f, 0f) : new Vector3(0.12f, 0f, 0f);
        var a = hands.Hand(ScriptedHands.AroundAxis(control.transform, axis, radius, from));
        hands.Grab(a, control);
        XRDirectInteractor b = null;
        if (twoHands)
        {
            b = hands.Hand(ScriptedHands.AroundAxis(control.transform, axis, -radius, from));
            hands.Grab(b, control);
        }

        yield return null;
        var step = Mathf.Sign(to - from) * 5f;
        for (var angle = from; Mathf.Abs(to - angle) > 0.01f; angle = Mathf.Abs(to - angle) < 5f ? to : angle + step)
        {
            a.transform.position = ScriptedHands.AroundAxis(control.transform, axis, radius, angle);
            if (b != null) b.transform.position = ScriptedHands.AroundAxis(control.transform, axis, -radius, angle);
            yield return null;
        }

        a.transform.position = ScriptedHands.AroundAxis(control.transform, axis, radius, to);
        if (b != null) b.transform.position = ScriptedHands.AroundAxis(control.transform, axis, -radius, to);
        yield return null;
        hands.Release(a, control);
        if (b != null) hands.Release(b, control);
        yield return null;
        Object.Destroy(a.gameObject);
        if (b != null) Object.Destroy(b.gameObject);
    }

    private IEnumerator MoveCover(RemovablePart cover, Vector3 to)
    {
        var hand = hands.Hand(cover.transform.position);
        hands.Grab(hand, cover);
        yield return null;
        var from = hand.transform.position;
        for (var t = 0f; t <= 1f; t += 0.1f)
        {
            hand.transform.position = Vector3.Lerp(from, to, t);
            yield return null;
        }

        hand.transform.position = to;
        yield return new WaitForSeconds(0.5f); // XRI eases the held object into the hand
        hands.Release(hand, cover);
        yield return null;
        Object.Destroy(hand.gameObject);
    }

    private IEnumerator WaitForPressure(float min, float max)
    {
        for (var t = 0f; t < 30f; t += Time.deltaTime)
        {
            var p = machine.Telemetry[TelemetryChannel.Pressure];
            if (p >= min && p <= max) yield break;
            yield return null;
        }

        Assert.Fail($"pressure never reached {min}-{max} bar (now {machine.Telemetry[TelemetryChannel.Pressure]:0.00})");
    }

    [UnityTest]
    public IEnumerator FullProcedure_WithRealControls_CompletesWithoutErrorsOrViolations()
    {
        var runner = machine.Runner;
        var skid = Object.FindAnyObjectByType<RemovablePart>().transform.root;
        StartButton().Press();
        yield return null;
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("inspect"));
        Assert.That(panel.TitleText, Is.EqualTo("Step 1 of 8: Inspect the relief valve"));

        yield return LookAt("relief_valve", 2f);
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("lockout"), "gaze dwell completes the inspection");

        yield return Turn(Rotary("main_breaker"), Vector3.back, 0f, 135f, twoHands: true);
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("close_inlet"));

        yield return Turn(Rotary("inlet_valve"), Vector3.down, 0f, 90f, twoHands: false);
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("verify_zero"));

        yield return LookAt("pressure_gauge", 0.1f); // too early: a hint, not an error
        yield return WaitForPressure(0f, 0.15f);
        yield return LookAt("pump", 0.1f);           // look away and back to read again
        yield return LookAt("pressure_gauge", 2f);
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("remove_cover"));

        var cover = Object.FindAnyObjectByType<RemovablePart>();
        yield return MoveCover(cover, cover.transform.position + skid.forward * 0.3f);
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("replace"));

        hands.Seat(Object.FindAnyObjectByType<ToolSocket>(), Object.FindAnyObjectByType<ToolItem>());
        yield return null;
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("restore"));
        Assert.That(machine.Telemetry.Faults.IsActive(FaultModel.Overpressure), Is.False, "the new cartridge fixed the fault");

        yield return MoveCover(cover, skid.position); // refit: released near its seat, it snaps on
        Assert.That(cover.State, Is.EqualTo(RemovablePart.Fitted));
        yield return Turn(Rotary("inlet_valve"), Vector3.down, 90f, 0f, twoHands: false);
        yield return Turn(Rotary("main_breaker"), Vector3.back, 135f, 0f, twoHands: true);
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("verify_running"));

        yield return WaitForPressure(3.2f, 4.8f);
        yield return LookAt("pressure_gauge", 2f);

        Assert.That(result, Is.Not.Null, "procedure completed");
        Assert.That(result.Errors.Select(e => e.Message), Is.Empty);
        Assert.That(result.Violations.Select(v => v.Id), Is.Empty);
        Assert.That(result.Passed, Is.True);
        Assert.That(panel.TitleText, Does.EndWith("passed"));
    }

    [UnityTest]
    public IEnumerator OpeningTheCoverBeforeLockout_IsASafetyViolation_OnThePanel()
    {
        StartButton().Press();
        yield return null;
        var cover = Object.FindAnyObjectByType<RemovablePart>();
        yield return MoveCover(cover, cover.transform.position + cover.transform.root.forward * 0.3f);

        Assert.That(machine.Runner.Violations.Select(v => v.Id), Does.Contain("loto_cover"));
        Assert.That(panel.StatusText, Does.StartWith("Safety: Lock out the breaker before opening the pump."));
    }

    [UnityTest]
    public IEnumerator RestartDuringARun_NeedsASecondPress()
    {
        var button = StartButton();
        button.Press();
        yield return LookAt("relief_valve", 2f);
        Assert.That(machine.Runner.CurrentStep.Id, Is.EqualTo("lockout"));

        yield return new WaitForSecondsRealtime(0.7f); // past the button's debounce
        button.Press();
        yield return null;
        Assert.That(machine.Runner.CurrentStep.Id, Is.EqualTo("lockout"), "one stray press never restarts a run");
        Assert.That(button.Label, Is.EqualTo("Confirm restart"));

        yield return new WaitForSecondsRealtime(0.7f);
        button.Press();
        yield return null;
        Assert.That(machine.Runner.CurrentStep.Id, Is.EqualTo("inspect"), "a confirming press restarts");
    }

    [UnityTest]
    public IEnumerator Gaze_SeesThePartThroughTriggersAndUntaggedColliders()
    {
        machine.TryGetPart("relief_valve", out var valve);
        var center = valve.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).center;
        var eye = center + valve.transform.root.forward * 0.8f;

        var trigger = GameObject.CreatePrimitive(PrimitiveType.Sphere); // like a hand's grab sphere
        trigger.GetComponent<Collider>().isTrigger = true;
        trigger.transform.position = Vector3.Lerp(eye, center, 0.3f);
        trigger.transform.localScale = Vector3.one * 0.1f;
        var roomMesh = GameObject.CreatePrimitive(PrimitiveType.Cube); // like a room-scan chunk in front of the part
        roomMesh.transform.position = Vector3.Lerp(eye, center, 0.6f);
        roomMesh.transform.localScale = new Vector3(0.3f, 0.3f, 0.01f);
        yield return new WaitForFixedUpdate();

        var seen = machine.PartAlong(new Ray(eye, (center - eye).normalized));
        Assert.That(seen?.Id, Is.EqualTo("relief_valve"));
    }
}
