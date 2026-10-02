using System.Collections;
using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Procedures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>The bench scene's machine controls, driven by scripted hands through XRI selection.</summary>
public class InteractionPlayModeTests
{
    private MachineServices machine;
    private XRInteractionManager manager;
    private ScriptedHands hands;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null; // Start: router discovers the controls
        machine = Object.FindAnyObjectByType<MachineServices>();
        manager = Object.FindAnyObjectByType<XRInteractionManager>();
        hands = new ScriptedHands(manager);
    }

    // The interlock holds parts outside a run too (#86): lock out and isolate first, as a technician would.
    private void Isolate()
    {
        machine.Runner.SetInitialState("main_breaker", "locked");
        machine.Runner.SetInitialState("inlet_valve", "closed");
    }

    private static RotaryInteractable Rotary(string partId) =>
        Object.FindObjectsByType<RotaryInteractable>(FindObjectsSortMode.None).Single(r => r.PartId == partId);

    [UnityTest]
    public IEnumerator Handwheel_TwoGearedTurnsClockwise_ClosesTheOutlet()
    {
        Isolate();
        // Two turns (720°) close it; the hand's travel counts 1.5×, so about 480° of hand movement is enough.
        var wheel = Rotary("outlet_valve");
        Assert.That(wheel.TurnGain, Is.EqualTo(1.5f).Within(0.01f));
        var hand = hands.Hand(ScriptedHands.AroundAxis(wheel.transform, Vector3.up, new Vector3(0.08f, 0f, 0f), 0f));
        hands.Grab(hand, wheel);
        yield return null;

        for (var a = 0f; a <= 495f; a += 15f)
        {
            hand.transform.position = ScriptedHands.AroundAxis(wheel.transform, Vector3.up, new Vector3(0.08f, 0f, 0f), a);
            yield return null;
        }

        Assert.That(wheel.State, Is.EqualTo("closed"));
        Assert.That(wheel.Angle, Is.EqualTo(720f).Within(0.5f));
        Assert.That(machine.Telemetry.Inputs.OutletOpening, Is.EqualTo(0f).Within(0.01f), "the twin follows the handwheel");
    }

    [UnityTest]
    public IEnumerator Breaker_OneHandCannotTurnIt_TwoHandsLockItOut()
    {
        var breaker = Rotary("main_breaker");
        var radius = new Vector3(0.12f, 0f, 0f);
        var left = hands.Hand(ScriptedHands.AroundAxis(breaker.transform, Vector3.back, -radius, 0f));
        hands.Grab(left, breaker);
        for (var a = 0f; a <= 90f; a += 10f)
        {
            left.transform.position = ScriptedHands.AroundAxis(breaker.transform, Vector3.back, -radius, a);
            yield return null;
        }

        Assert.That(breaker.Angle, Is.EqualTo(0f), "one hand does nothing on a two-hand isolator");
        left.transform.position = ScriptedHands.AroundAxis(breaker.transform, Vector3.back, -radius, 0f);

        var right = hands.Hand(ScriptedHands.AroundAxis(breaker.transform, Vector3.back, radius, 0f));
        hands.Grab(right, breaker);
        yield return null;
        for (var a = 0f; a <= 135f; a += 5f)
        {
            left.transform.position = ScriptedHands.AroundAxis(breaker.transform, Vector3.back, -radius, a);
            right.transform.position = ScriptedHands.AroundAxis(breaker.transform, Vector3.back, radius, a);
            yield return null;
        }

        Assert.That(breaker.State, Is.EqualTo("locked"));
        Assert.That(machine.Telemetry.Inputs.Powered, Is.False, "locked out: motor off");
        Assert.That(machine.Runner.GetPartState("main_breaker"), Is.EqualTo("locked"), "runner knows before a procedure starts");
    }

    [UnityTest]
    public IEnumerator Lever_ReleasedNearClosed_SnapsClosed()
    {
        Isolate();
        var lever = Rotary("inlet_valve");
        var radius = new Vector3(0.1f, 0f, 0f);
        var hand = hands.Hand(ScriptedHands.AroundAxis(lever.transform, Vector3.down, radius, 0f));
        hands.Grab(hand, lever);
        yield return null;
        for (var a = 0f; a <= 75f; a += 5f)
        {
            hand.transform.position = ScriptedHands.AroundAxis(lever.transform, Vector3.down, radius, a);
            yield return null;
        }

        hands.Release(hand, lever);
        yield return null;

        Assert.That(lever.Angle, Is.EqualTo(90f), "released 15° short: snaps into the closed detent");
        Assert.That(lever.State, Is.EqualTo("closed"));
        Assert.That(machine.Telemetry.Inputs.InletOpening, Is.EqualTo(0f).Within(0.01f));
    }

    [UnityTest]
    public IEnumerator Cover_PulledOff_IsRemoved_AndOpensTheSeat()
    {
        Isolate();
        var cover = Object.FindAnyObjectByType<RemovablePart>();
        var socket = Object.FindAnyObjectByType<ToolSocket>();
        Assert.That(socket.socketActive, Is.False, "seat is closed while the cover is on");

        var start = cover.Centre;
        var away = cover.transform.parent.forward; // the skid's front; read before the grab (XRI unparents held objects)
        var hand = hands.Hand(start);
        hands.Grab(hand, cover);
        yield return null;
        for (var d = 0f; d <= 0.25f; d += 0.02f)
        {
            hand.transform.position = start + away * d;
            yield return null;
        }

        yield return new WaitForSeconds(0.3f); // XRI eases a grabbed object into the hand (attachEaseInTime)

        Assert.That(cover.State, Is.EqualTo(RemovablePart.Removed));
        Assert.That(socket.socketActive, Is.True);
    }

    /// <summary>Pulls the cover <paramref name="distance"/> straight out, held at its centre; returns the hand.</summary>
    private IEnumerator PullCover(RemovablePart cover, XRDirectInteractor hand, Vector3 start, Vector3 away, float distance)
    {
        for (var d = 0f; d <= distance; d += 0.02f)
        {
            hand.transform.position = start + away * d;
            yield return null;
        }

        hand.transform.position = start + away * distance;
        yield return new WaitForSeconds(0.3f);
    }

    [UnityTest]
    public IEnumerator Cover_GrabbedAtAnAngle_StaysPut_AndTurnsWithTheHand()
    {
        Isolate();
        var cover = Object.FindAnyObjectByType<RemovablePart>();
        var position = cover.transform.position;
        var rotation = cover.transform.rotation;
        var hand = hands.Hand(cover.Centre + new Vector3(0.03f, 0.02f, 0f));
        hand.transform.rotation = Quaternion.Euler(20f, 60f, 35f); // a wrist at an odd angle
        hands.Grab(hand, cover);
        yield return new WaitForSeconds(0.3f);

        Assert.That(Vector3.Distance(cover.transform.position, position), Is.LessThan(0.002f), "#67: no jump to the hand on grab");
        Assert.That(Quaternion.Angle(cover.transform.rotation, rotation), Is.LessThan(0.5f), "#67: no twist on grab");

        var turn = Quaternion.AngleAxis(30f, Vector3.up);
        hand.transform.rotation = turn * hand.transform.rotation;
        yield return new WaitForSeconds(0.3f);
        Assert.That(Quaternion.Angle(cover.transform.rotation, turn * rotation), Is.LessThan(1f), "turns with the hand");
        hands.Release(hand, cover);
        Object.Destroy(hand.gameObject);
    }

    [UnityTest]
    public IEnumerator Cover_BroughtBackWhileHeld_ClicksOn()
    {
        Isolate();
        var cover = Object.FindAnyObjectByType<RemovablePart>();
        var start = cover.Centre;
        var away = cover.transform.parent.forward;
        var hand = hands.Hand(start);
        hands.Grab(hand, cover);
        yield return PullCover(cover, hand, start, away, 0.25f);
        Assert.That(cover.State, Is.EqualTo(RemovablePart.Removed));

        hand.transform.position = start + away * 0.1f;
        yield return new WaitForSeconds(0.3f);
        Assert.That(cover.State, Is.EqualTo(RemovablePart.Removed), "10 cm out and still held: not yet");

        hand.transform.position = start + away * 0.03f;
        yield return null;
        yield return null;
        Assert.That(cover.State, Is.EqualTo(RemovablePart.Fitted), "#67: within 5 cm it clicks on by itself");
        Assert.That(cover.Offset, Is.LessThan(0.001f));
        if (hand.IsSelecting(cover)) hands.Release(hand, cover);
        Object.Destroy(hand.gameObject);
    }

    [UnityTest]
    public IEnumerator Cover_ReleasedNearItsSeat_GoesBackOn()
    {
        Isolate();
        var cover = Object.FindAnyObjectByType<RemovablePart>();
        var start = cover.Centre;
        var away = cover.transform.parent.forward;
        var side = cover.transform.parent.right; // read before the grab: XRI unparents held objects
        var scale = cover.transform.parent.lossyScale.x; // the cover measures in the machine's space
        var hand = hands.Hand(start);
        hands.Grab(hand, cover);
        yield return PullCover(cover, hand, start, away, 0.25f);
        hand.transform.position = start + (away * 0.12f + side * 0.04f) * scale;
        yield return new WaitForSeconds(0.3f);
        hands.Release(hand, cover);
        yield return null;

        Assert.That(cover.State, Is.EqualTo(RemovablePart.Fitted), "#67: released 13 cm from its seat, it goes back on");
        Assert.That(cover.Offset, Is.LessThan(0.001f));
        Object.Destroy(hand.gameObject);
    }

    [UnityTest]
    public IEnumerator Cartridge_Seated_SitsInsideThePump_BehindTheCover()
    {
        Isolate();
        var socket = Object.FindAnyObjectByType<ToolSocket>();
        socket.socketActive = true;
        var cartridge = Object.FindAnyObjectByType<ToolItem>();
        hands.Seat(socket, cartridge);
        yield return new WaitForSeconds(0.3f);

        var machine = Object.FindAnyObjectByType<RemovablePart>().transform.parent;
        var along = machine.InverseTransformDirection(cartridge.transform.forward);
        Assert.That(Mathf.Abs(along.z), Is.GreaterThan(0.99f), "points into the pump");
        var centre = machine.InverseTransformPoint(cartridge.GetComponentInChildren<Renderer>().bounds.center);
        // 12 cm long: its outer end must not pass the seat face (z 0.10), where the refitted cover begins.
        Assert.That(centre.z + 0.06f, Is.LessThanOrEqualTo(0.101f), $"#67: the seated cartridge ends at z {centre.z + 0.06f:0.000}");
        Assert.That(centre.z - 0.06f, Is.GreaterThan(-0.1f), "and stays inside the pump body");
    }

    [UnityTest]
    public IEnumerator Cartridge_InTheSeat_CompletesTheToolStep()
    {
        var runner = machine.Runner;
        runner.Start(machine.Now);
        runner.Handle(InteractionEvent.Gaze(machine.Now, "relief_valve", 2f));
        runner.Handle(InteractionEvent.State(machine.Now, "main_breaker", "locked"));
        runner.Handle(InteractionEvent.State(machine.Now, "inlet_valve", "closed"));
        runner.Handle(InteractionEvent.Measured(machine.Now, 0f));
        runner.Handle(InteractionEvent.State(machine.Now, "pump_cover", "removed"));
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("replace"));

        var socket = Object.FindAnyObjectByType<ToolSocket>();
        socket.socketActive = true;
        var cartridge = Object.FindAnyObjectByType<ToolItem>();
        hands.Seat(socket, cartridge);
        yield return null;

        Assert.That(socket.Seated, Is.SameAs(cartridge));
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("refit_cover"), "socketing the new cartridge completes 'replace'");
    }

    [UnityTest]
    public IEnumerator EveryControl_BrightensWhenAHandIsInReach()
    {
        var controls = Object.FindObjectsByType<XRBaseInteractable>(FindObjectsSortMode.None);
        Assert.That(controls.Length, Is.GreaterThanOrEqualTo(5), "lever, handwheel, breaker, cover, cartridge");
        foreach (var control in controls)
        {
            Assert.That(control.GetComponent<HoverTint>(), Is.Not.Null, $"{control.name} shows it can be grabbed");
        }

        var wheel = Rotary("outlet_valve");
        var hand = hands.Hand(wheel.transform.position);
        manager.HoverEnter((IXRHoverInteractor)hand, wheel); // events are synchronous; XRI re-validates hovers next frame
        Assert.That(wheel.GetComponent<HoverTint>().IsTinted, Is.True);
        manager.HoverExit((IXRHoverInteractor)hand, wheel);
        Assert.That(wheel.GetComponent<HoverTint>().IsTinted, Is.False);
        yield break;
    }
}
