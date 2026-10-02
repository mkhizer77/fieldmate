using System.Collections;
using System.Linq;
using Fieldmate.Interaction;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Tests.PlayMode.UI;

/// <summary>Device test 2026-10-01: with controllers the ray reaches the machine's controls; with hands only buttons.</summary>
public class RayReachPlayModeTests
{
    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        yield return Assistant.ProcedurePlayModeTests.PlaceMachine();
    }

    [TearDown]
    public void ResetModality() => InputModalityProbe.Set(Modality.Hands);

    [UnityTest]
    public IEnumerator Controllers_reach_the_inlet_valve_from_a_distance_hands_do_not()
    {
        var lever = Object.FindObjectsByType<RotaryInteractable>(FindObjectsSortMode.None).Single(r => r.PartId == "inlet_valve");
        var pointer = GameObject.Find("Right Pointer");
        var ray = pointer.GetComponent<XRRayInteractor>();
        var target = lever.GetComponentInChildren<Collider>().bounds.center;
        pointer.transform.position = target + (Camera.main.transform.position - target).normalized * 1.5f;
        pointer.transform.rotation = Quaternion.LookRotation(target - pointer.transform.position);

        InputModalityProbe.Set(Modality.Controllers);
        for (var i = 0; i < 5; i++) yield return null;
        Assert.That(ray.interactablesHovered.Contains(lever), Is.True, "controllers: the ray reaches the valve 1.5 m away");

        InputModalityProbe.Set(Modality.Hands);
        for (var i = 0; i < 5; i++) yield return null;
        Assert.That(ray.interactablesHovered.Contains(lever), Is.False, "hands: reach in to operate parts; the ray presses buttons");
    }

    [UnityTest]
    public IEnumerator Controllers_latch_onto_a_part_when_pointing_near_it_not_only_through_it()
    {
        var lever = Object.FindObjectsByType<RotaryInteractable>(FindObjectsSortMode.None).Single(r => r.PartId == "inlet_valve");
        var pointer = GameObject.Find("Right Pointer");
        var ray = pointer.GetComponent<XRRayInteractor>();
        var target = lever.GetComponentInChildren<Collider>().bounds.center;
        pointer.transform.position = target + (Camera.main.transform.position - target).normalized * 1.5f;
        // Aim 10 cm beside the lever: a plain ray misses it (device test 2026-10-02: "only at the exact centre").
        var beside = target + Vector3.Cross(Vector3.up, (target - pointer.transform.position).normalized) * 0.1f;
        pointer.transform.rotation = Quaternion.LookRotation(beside - pointer.transform.position);

        InputModalityProbe.Set(Modality.Controllers);
        for (var i = 0; i < 5; i++) yield return null;
        Assert.That(ray.interactablesHovered.Contains(lever), Is.True, "pointing near the valve is enough to grab it");

        ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast; // the old plain ray, for comparison
        for (var i = 0; i < 5; i++) yield return null;
        Assert.That(ray.interactablesHovered.Contains(lever), Is.False, "a plain ray needed the exact line through the lever");
    }
}
