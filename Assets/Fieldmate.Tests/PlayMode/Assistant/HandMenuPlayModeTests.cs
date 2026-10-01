using System.Collections;
using System.Linq;
using Fieldmate.Interaction;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#73: the app's controls live in a hand menu on the left hand, not on the machine.</summary>
public class HandMenuPlayModeTests
{
    private HandMenu menu;
    private PalmPose palm;
    private Transform head;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        menu = Object.FindAnyObjectByType<HandMenu>();
        palm = Object.FindAnyObjectByType<PalmPose>();
        head = Camera.main.transform;
    }

    [TearDown]
    public void ResetLabels() => ControlTag.ShowAll = false;

    private static PressButton Row(string name) =>
        Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == name);

    [UnityTest]
    public IEnumerator Buttons_LeaveTheMachine_ForAClosedMenu()
    {
        var skid = Object.FindAnyObjectByType<RemovablePart>().transform.root;
        Assert.That(skid.GetComponentsInChildren<PressButton>(true), Is.Empty, "nothing on the machine");
        yield return null;
        Assert.That(menu.IsOpen, Is.False);
        Assert.That(menu.GetComponentsInChildren<Collider>().All(c => !c.enabled), Is.True, "closed: rows can't be pressed by accident");
        Assert.That(menu.GetComponentsInChildren<Canvas>().All(c => !c.enabled), Is.True);
        Assert.That(Row("Start Button").transform.IsChildOf(menu.transform), Is.True);
        Assert.That(Row("Labels Button").transform.IsChildOf(menu.transform), Is.True);
    }

    [UnityTest]
    public IEnumerator PalmTowardsTheFace_OpensItBesideThePalm_AndTurningAwayClosesItAfterAMoment()
    {
        var palmAt = head.position + head.forward * 0.35f + Vector3.down * 0.35f + head.right * -0.15f;
        var towardsFace = (head.position - palmAt).normalized;
        palm.Simulate(palmAt, Quaternion.LookRotation(Vector3.Cross(towardsFace, Vector3.right), -towardsFace), tracked: true);
        yield return null;
        yield return null;
        Assert.That(menu.IsOpen, Is.True, "palm up opens it");
        Assert.That(Vector3.Distance(menu.transform.position, palmAt), Is.LessThan(0.25f), "beside the palm");
        Assert.That(menu.GetComponentsInChildren<Collider>().Any(c => c.enabled), Is.True);

        palm.Simulate(palmAt, Quaternion.LookRotation(Vector3.Cross(-towardsFace, Vector3.right), towardsFace), tracked: true);
        yield return new WaitForSeconds(0.5f);
        Assert.That(menu.IsOpen, Is.True, "stays a moment so the other hand can press a row");
        yield return new WaitForSeconds(1.5f);
        Assert.That(menu.IsOpen, Is.False);
    }

    [UnityTest]
    public IEnumerator MenuButton_Toggles_AndTheLabelsRowShowsEveryLabel()
    {
        menu.Toggle();
        yield return null;
        Assert.That(menu.IsOpen, Is.True, "the left controller's menu button opens it");

        var labels = Row("Labels Button");
        Assert.That(labels.Label, Is.EqualTo("Labels: Off"));
        labels.Press();
        yield return null;
        Assert.That(ControlTag.ShowAll, Is.True);
        Assert.That(labels.Label, Is.EqualTo("Labels: On"));

        menu.Toggle();
        yield return null;
        Assert.That(menu.IsOpen, Is.False);
    }
}
