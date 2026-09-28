using Fieldmate.Assistant;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Assistant;

/// <summary>Timings replay what the Quest logged on 2026-09-28 with hand-pinch push-to-talk.</summary>
public class TalkGateTests
{
    private TalkGate gate;

    [SetUp]
    public void SetUp() => gate = new TalkGate();

    private void Ask(float pressAt, float releaseAt)
    {
        Assert.That(gate.Press(pressAt, assistantBusy: false), Is.EqualTo(TalkAction.Listen));
        gate.Release(releaseAt);
        Assert.That(gate.Tick(releaseAt + TalkGate.ReleaseGraceSeconds), Is.EqualTo(TalkAction.Finish));
    }

    [Test]
    public void StrayPressRightAfterAQuestion_DoesNotInterruptIt()
    {
        Ask(0f, 2.6f);

        // Device: a pinch flicker 0.08 s after the recording finished, while transcribing.
        Assert.That(gate.Press(2.93f, assistantBusy: true), Is.EqualTo(TalkAction.StartPending));
        Assert.That(gate.Release(3.05f), Is.EqualTo(TalkAction.DropStray));
        Assert.That(gate.Tick(10f), Is.EqualTo(TalkAction.None), "nothing is interrupted later either");
        Assert.That(gate.IsRecording, Is.False);
    }

    [Test]
    public void PinchHeldDuringTheReplyWithoutSpeaking_NeverInterrupts()
    {
        // Device 11:08:34: a resting pinch held ~0.4 s cut the answer off although the user said nothing.
        gate.Press(34.19f, assistantBusy: true);
        for (var t = 34.2f; t < 36f; t += 0.1f)
        {
            Assert.That(gate.Tick(t), Is.EqualTo(TalkAction.None), "holding alone never interrupts");
        }

        Assert.That(gate.IsPending, Is.True);
    }

    [Test]
    public void ShortPendingPress_IsDropped_LongOne_IsCheckedWithSpeechToText()
    {
        gate.Press(0f, assistantBusy: true);
        Assert.That(gate.Release(0.4f), Is.EqualTo(TalkAction.DropStray));

        gate.Press(5f, assistantBusy: true);
        Assert.That(gate.Release(5f + TalkGate.MinSpeechSeconds + 0.01f), Is.EqualTo(TalkAction.CheckSpeech));
    }

    [Test]
    public void SpeechDuringAPendingPress_Interrupts_ThenRecordsNormally()
    {
        gate.Press(10f, assistantBusy: true);
        Assert.That(gate.SpeechDetected(), Is.EqualTo(TalkAction.Interrupt));
        Assert.That(gate.SpeechDetected(), Is.EqualTo(TalkAction.None), "interrupts once");

        Assert.That(gate.Release(12f), Is.EqualTo(TalkAction.None));
        Assert.That(gate.Tick(12f + TalkGate.ReleaseGraceSeconds), Is.EqualTo(TalkAction.Finish));
    }

    [Test]
    public void SpeechWhileListeningNormally_IsNotAnInterrupt()
    {
        gate.Press(0f, assistantBusy: false);
        Assert.That(gate.SpeechDetected(), Is.EqualTo(TalkAction.None));
    }

    [Test]
    public void FlickerReleaseMidSentence_KeepsRecording()
    {
        gate.Press(0f, assistantBusy: false);
        gate.Release(1.0f);
        Assert.That(gate.Press(1.1f, assistantBusy: false), Is.EqualTo(TalkAction.None), "re-press within grace continues");
        Assert.That(gate.Tick(2f), Is.EqualTo(TalkAction.None));
        Assert.That(gate.IsRecording, Is.True);
    }

    [Test]
    public void Reset_AllowsANewPress()
    {
        gate.Press(0f, assistantBusy: true);
        gate.Reset();
        Assert.That(gate.IsPending, Is.False);
        Assert.That(gate.Press(6f, assistantBusy: false), Is.EqualTo(TalkAction.Listen));
    }
}
