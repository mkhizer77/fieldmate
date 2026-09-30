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
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>The bench scene's machine controls, driven by scripted hands through XRI selection.</summary>
public class InteractionPlayModeTests
{
    private MachineServices machine;
    private XRInteractionManager manager;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null; // Start: router discovers the controls
        machine = Object.FindAnyObjectByType<MachineServices>();
        manager = Object.FindAnyObjectByType<XRInteractionManager>();
    }

    private static RotaryInteractable Rotary(string partId) =>
        Object.FindObjectsByType<RotaryInteractable>(FindObjectsSortMode.None).Single(r => r.PartId == partId);

    /// <summary>A scripted hand: a direct interactor whose select button is held.</summary>
    private XRDirectInteractor Hand(Vector3 position)
    {
        var go = new GameObject("Test Hand", typeof(SphereCollider), typeof(Rigidbody));
        go.GetComponent<SphereCollider>().isTrigger = true;
        go.GetComponent<SphereCollider>().radius = 0.01f;
        go.GetComponent<Rigidbody>().isKinematic = true;
        go.transform.position = position;
        var hand = go.AddComponent<XRDirectInteractor>();
        hand.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
        hand.selectInput.manualPerformed = true;
        hand.selectInput.manualValue = 1f;
        return hand;
    }

    private void Grab(XRBaseInteractor hand, IXRSelectInteractable target) => manager.SelectEnter((IXRSelectInteractor)hand, target);

    private static Vector3 AroundAxis(Transform pivot, Vector3 localAxis, Vector3 localRadius, float degrees)
    {
        var space = pivot.parent;
        return space.TransformPoint(pivot.localPosition + Quaternion.AngleAxis(degrees, localAxis) * localRadius);
    }

    [UnityTest]
    public IEnumerator Handwheel_ThreeTurnsClockwise_ClosesTheOutlet()
    {
        var wheel = Rotary("outlet_valve");
        var hand = Hand(AroundAxis(wheel.transform, Vector3.up, new Vector3(0.08f, 0f, 0f), 0f));
        Grab(hand, wheel);
        yield return null;

        for (var a = 0f; a <= 1090f; a += 15f)
        {
            hand.transform.position = AroundAxis(wheel.transform, Vector3.up, new Vector3(0.08f, 0f, 0f), a);
            yield return null;
        }

        Assert.That(wheel.State, Is.EqualTo("closed"));
        Assert.That(wheel.Angle, Is.EqualTo(1080f).Within(0.5f));
        Assert.That(machine.Telemetry.Inputs.OutletOpening, Is.EqualTo(0f).Within(0.01f), "the twin follows the handwheel");
    }

    [UnityTest]
    public IEnumerator Breaker_OneHandCannotTurnIt_TwoHandsLockItOut()
    {
        var breaker = Rotary("main_breaker");
        var radius = new Vector3(0.12f, 0f, 0f);
        var left = Hand(AroundAxis(breaker.transform, Vector3.back, -radius, 0f));
        Grab(left, breaker);
        for (var a = 0f; a <= 90f; a += 10f)
        {
            left.transform.position = AroundAxis(breaker.transform, Vector3.back, -radius, a);
            yield return null;
        }

        Assert.That(breaker.Angle, Is.EqualTo(0f), "one hand does nothing on a two-hand isolator");
        left.transform.position = AroundAxis(breaker.transform, Vector3.back, -radius, 0f);

        var right = Hand(AroundAxis(breaker.transform, Vector3.back, radius, 0f));
        Grab(right, breaker);
        yield return null;
        for (var a = 0f; a <= 135f; a += 5f)
        {
            left.transform.position = AroundAxis(breaker.transform, Vector3.back, -radius, a);
            right.transform.position = AroundAxis(breaker.transform, Vector3.back, radius, a);
            yield return null;
        }

        Assert.That(breaker.State, Is.EqualTo("locked"));
        Assert.That(machine.Telemetry.Inputs.Powered, Is.False, "locked out: motor off");
        Assert.That(machine.Runner.GetPartState("main_breaker"), Is.EqualTo("locked"), "runner knows before a procedure starts");
    }

    [UnityTest]
    public IEnumerator Lever_ReleasedNearClosed_SnapsClosed()
    {
        var lever = Rotary("inlet_valve");
        var radius = new Vector3(0.1f, 0f, 0f);
        var hand = Hand(AroundAxis(lever.transform, Vector3.down, radius, 0f));
        Grab(hand, lever);
        yield return null;
        for (var a = 0f; a <= 75f; a += 5f)
        {
            hand.transform.position = AroundAxis(lever.transform, Vector3.down, radius, a);
            yield return null;
        }

        manager.SelectExit((IXRSelectInteractor)hand, lever);
        yield return null;

        Assert.That(lever.Angle, Is.EqualTo(90f), "released 15° short: snaps into the closed detent");
        Assert.That(lever.State, Is.EqualTo("closed"));
        Assert.That(machine.Telemetry.Inputs.InletOpening, Is.EqualTo(0f).Within(0.01f));
    }

    [UnityTest]
    public IEnumerator Cover_PulledOff_IsRemoved_AndOpensTheSeat()
    {
        var cover = Object.FindAnyObjectByType<RemovablePart>();
        var socket = Object.FindAnyObjectByType<ToolSocket>();
        Assert.That(socket.socketActive, Is.False, "seat is closed while the cover is on");

        var start = cover.transform.position;
        var away = cover.transform.parent.forward; // the skid's front; read before the grab (XRI unparents held objects)
        var hand = Hand(start);
        Grab(hand, cover);
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
        manager.SelectEnter((IXRSelectInteractor)socket, cartridge);
        yield return null;

        Assert.That(socket.Seated, Is.SameAs(cartridge));
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("restore"), "socketing the new cartridge completes 'replace'");
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
        var hand = Hand(wheel.transform.position);
        manager.HoverEnter((IXRHoverInteractor)hand, wheel); // events are synchronous; XRI re-validates hovers next frame
        Assert.That(wheel.GetComponent<HoverTint>().IsTinted, Is.True);
        manager.HoverExit((IXRHoverInteractor)hand, wheel);
        Assert.That(wheel.GetComponent<HoverTint>().IsTinted, Is.False);
        yield break;
    }
}
