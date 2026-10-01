using System.Collections;
using Fieldmate.AI;
using Fieldmate.Assistant;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#71 device test: the mate's bubble shows the spoken line word by word, beside the mate on the user's left.</summary>
public class SpeechToastPlayModeTests
{
    private sealed class FakeSpeech : ISpeechOutput
    {
        public bool IsPlaying { get; set; }
        public float OutputRms() => IsPlaying ? 0.08f : 0f;
    }

    private const string Line = "Step one done. Next, lock out the main breaker with both hands and turn it to locked.";
    private GameObject user;
    private HologramMate mate;
    private AssistantPanel panel;
    private FakeSpeech speech;

    [SetUp]
    public void SetUp()
    {
        user = new GameObject("User Head");
        user.transform.position = new Vector3(0f, 1.6f, 0f);
        mate = new GameObject("Mate", typeof(HologramMate)).GetComponent<HologramMate>();
        mate.Place(new Vector3(0f, 1.1f, 1f));
        speech = new FakeSpeech();
        mate.UseSpeech(speech);
        panel = new GameObject("Bubble", typeof(RectTransform), typeof(AssistantPanel)).GetComponent<AssistantPanel>();
        panel.FollowMate(mate);
        panel.SetHead(user.transform);
    }

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(panel.gameObject);
        Object.Destroy(mate.gameObject);
        Object.Destroy(user);
    }

    [UnityTest]
    public IEnumerator Spoken_line_appears_word_by_word_with_the_voice()
    {
        speech.IsPlaying = true;
        panel.Add(new TranscriptEntry(TranscriptKind.Assistant, Line));
        yield return new WaitForSeconds(1f);
        var partway = panel.VisibleCharacters;
        Assert.That(partway, Is.InRange(8, 30), "about 15 characters a second of speech");
        Assert.That(partway == Line.Length || Line[partway] == ' ', Is.True, "whole words only");
        Assert.That(panel.ToastAlpha, Is.GreaterThan(0.9f));

        speech.IsPlaying = false;
        yield return null;
        yield return null;
        Assert.That(panel.VisibleCharacters, Is.EqualTo(Line.Length), "the voice has finished: the whole line");
        Assert.That(panel.TranscriptText, Does.Contain(Line));
    }

    [UnityTest]
    public IEnumerator Without_a_voice_it_reveals_at_reading_pace()
    {
        panel.Add(new TranscriptEntry(TranscriptKind.Assistant, Line));
        yield return new WaitForSeconds(1f);
        Assert.That(panel.VisibleCharacters, Is.EqualTo(0), "waits briefly for the voice");
        yield return new WaitForSeconds(4.5f);
        Assert.That(panel.VisibleCharacters, Is.EqualTo(Line.Length));
    }

    [UnityTest]
    public IEnumerator Sits_on_the_users_left_of_the_mate_and_fades_when_quiet()
    {
        panel.Add(new TranscriptEntry(TranscriptKind.User, "where is the breaker"));
        yield return null;
        yield return null;
        var toPanel = panel.transform.position - mate.HeadPivot.position;
        Assert.That(Vector3.Dot(toPanel, Vector3.left), Is.GreaterThan(0.1f), "the user faces +z: their left is -x");
        Assert.That(panel.MessageText, Is.EqualTo("where is the breaker"));

        yield return new WaitForSeconds(7f);
        Assert.That(panel.ToastAlpha, Is.EqualTo(0f), "quiet for a while: the bubble fades out");
    }
}
