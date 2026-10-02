using System.Collections;
using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Tests.PlayMode.UI;

/// <summary>
/// Device test 2026-10-02 (#86): a controller ray held levers at the pivot, and both rays on the breaker took its centre.
/// A ray now takes hold at the handle's grip points: the lever's end, opposite ends of the breaker bar.
/// </summary>
public class RayGripPlayModeTests
{
    private MachineServices machine;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        yield return Assistant.ProcedurePlayModeTests.PlaceMachine();
        machine = Object.FindAnyObjectByType<MachineServices>();
        InputModalityProbe.Set(Modality.Controllers);
    }

    [TearDown]
    public void ResetModality() => InputModalityProbe.Set(Modality.Hands);

    private static RotaryInteractable Rotary(string partId) =>
        Object.FindObjectsByType<RotaryInteractable>(FindObjectsSortMode.None).Single(r => r.PartId == partId);

    // A pointer 1.5 m from the part towards the user, aimed at it, with its trigger held.
    private static XRRayInteractor AimAndHold(string side, Vector3 target, Vector3 sideways)
    {
        var pointer = GameObject.Find($"{side} Pointer");
        pointer.transform.position = target + (Camera.main.transform.position - target).normalized * 1.5f + sideways;
        pointer.transform.rotation = Quaternion.LookRotation(target - pointer.transform.position);
        var ray = pointer.GetComponent<XRRayInteractor>();
        ray.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
        ray.selectInput.manualPerformed = true;
        ray.selectInput.manualValue = 1f;
        return ray;
    }

    [UnityTest]
    public IEnumerator A_ray_holds_the_lever_at_its_end_and_drags_it_closed()
    {
        machine.Runner.SetInitialState("main_breaker", "locked"); // the interlock holds the inlet on a live machine
        var lever = Rotary("inlet_valve");
        var ray = AimAndHold("Right", lever.GetComponentInChildren<Collider>().bounds.center, Vector3.zero);
        for (var i = 0; i < 5; i++) yield return null;

        Assert.That(lever.isSelected, Is.True, "trigger held while pointing at the lever");
        var grip = lever.GetAttachTransform(ray);
        Assert.That(grip, Is.Not.SameAs(lever.transform), "the line bends to a grip point, not to the pivot");
        Assert.That(Vector3.Distance(grip.position, lever.Handle.position), Is.GreaterThan(0.1f), "at the lever's end");
        Assert.That(Vector3.Distance(ray.attachTransform.position, grip.position), Is.LessThan(0.005f));

        // Sweep the controller so its held point follows the lever's arc a quarter turn round.
        var handle = lever.Handle;
        var radius = handle.parent.InverseTransformPoint(grip.position) - handle.localPosition;
        var pointer = ray.transform;
        for (var a = 0f; a <= 80f; a += 4f)
        {
            var target = handle.parent.TransformPoint(handle.localPosition + Quaternion.AngleAxis(a, lever.Axis) * radius);
            pointer.position += target - ray.attachTransform.position;
            yield return null;
        }

        ray.selectInput.manualPerformed = false;
        ray.selectInput.manualValue = 0f;
        for (var i = 0; i < 40; i++) yield return null; // past the grip grace period

        Assert.That(lever.State, Is.EqualTo("closed"));
    }

    [UnityTest]
    public IEnumerator Two_rays_on_the_breaker_take_opposite_ends_of_the_bar()
    {
        var breaker = Rotary("main_breaker");
        var centre = breaker.GetComponentInChildren<Collider>().bounds.center;
        var right = AimAndHold("Right", centre, Vector3.zero);
        for (var i = 0; i < 5; i++) yield return null;
        var left = AimAndHold("Left", centre, Vector3.zero);
        for (var i = 0; i < 5; i++) yield return null;

        Assert.That(breaker.HandsHolding, Is.EqualTo(2));
        var a = breaker.GetAttachTransform(right).position;
        var b = breaker.GetAttachTransform(left).position;
        Assert.That(Vector3.Distance(a, b), Is.GreaterThan(0.18f), "one ray on each end, not both in the middle");
        Assert.That(Vector3.Distance((a + b) * 0.5f, breaker.Handle.position), Is.LessThan(0.02f), "either side of the pivot");
    }
}
