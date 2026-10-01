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
}
