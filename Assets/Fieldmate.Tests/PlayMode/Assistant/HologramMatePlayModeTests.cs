using System.Collections;
using System.Linq;
using Fieldmate.AI;
using Fieldmate.Assistant;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#69: the hologram mate is built, looks at the user, and moves its lips with the speech that plays.</summary>
public class HologramMatePlayModeTests
{
    private sealed class FakeSpeech : ISpeechOutput
    {
        public bool IsPlaying { get; set; }
        public float Rms { get; set; }
        public float OutputRms() => Rms;
    }

    private GameObject root;
    private GameObject user;
    private HologramMate mate;
    private FakeSpeech speech;

    [SetUp]
    public void SetUp()
    {
        user = new GameObject("User Head");
        user.transform.position = new Vector3(0f, 1.6f, 1.5f);
        root = new GameObject("Hologram", typeof(HologramMate));
        mate = root.GetComponent<HologramMate>();
        mate.SetHead(user.transform);
        speech = new FakeSpeech();
        mate.UseSpeech(speech);
    }

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(root);
        Object.Destroy(user);
    }

    [UnityTest]
    public IEnumerator Figure_IsHalfABody_OnAPedestal_InHologramLight()
    {
        yield return null;
        var names = root.GetComponentsInChildren<Renderer>().Select(r => r.name).ToArray();
        Assert.That(names, Is.SupersetOf(new[] { "Pedestal", "Torso", "Skull", "Left Eye", "Right Eye", "Upper Lip", "Lower Lip", "Left Arm", "Right Arm" }));
        foreach (var renderer in root.GetComponentsInChildren<Renderer>().Where(r => r.name != "Pedestal"))
        {
            Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Fieldmate/Hologram"), renderer.name);
        }

        var headY = mate.HeadPivot.position.y;
        Assert.That(headY, Is.InRange(1.5f, 1.75f), "eye line at a standing adult's eye height");
        Assert.That(mate.Figure.position.y, Is.GreaterThan(HologramMate.PedestalHeight), "the figure starts at the waist, above the projector");
    }

    [UnityTest]
    public IEnumerator Lips_OpenWithSpeech_AndCloseWhenItStops()
    {
        yield return null;
        Assert.That(mate.LipGap, Is.LessThan(0.005f), "closed while silent");

        speech.IsPlaying = true;
        speech.Rms = 0.12f;
        mate.SetState(AssistantState.Speaking);
        yield return new WaitForSeconds(0.25f);
        Assert.That(mate.MouthOpen, Is.GreaterThan(0.9f));
        Assert.That(mate.LipGap, Is.GreaterThan(0.018f), "the lower lip drops about 2 cm");

        speech.Rms = 0f;
        yield return new WaitForSeconds(0.3f);
        Assert.That(mate.MouthOpen, Is.LessThan(0.05f), "a pause in the speech closes the mouth");

        speech.Rms = 0.12f;
        yield return new WaitForSeconds(0.2f);
        speech.IsPlaying = false; // interrupted
        mate.SetState(AssistantState.Idle);
        yield return null;
        Assert.That(mate.MouthOpen, Is.EqualTo(0f), "an interrupt shuts it at once");
    }

    [UnityTest]
    public IEnumerator Figure_TurnsToFaceTheUser()
    {
        user.transform.position = new Vector3(1.5f, 1.6f, 0f); // off to its right
        yield return new WaitForSeconds(4f);
        var toUser = user.transform.position - mate.Figure.position;
        toUser.y = 0f;
        Assert.That(Vector3.Dot(mate.Figure.forward, toUser.normalized), Is.GreaterThan(0.97f));
        Assert.That(Vector3.Dot(mate.HeadPivot.forward, (user.transform.position - mate.HeadPivot.position).normalized), Is.GreaterThan(0.97f),
            "the head looks at the user's eyes");
    }
}
