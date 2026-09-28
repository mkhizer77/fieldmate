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
        Assert.That(gate.Tick(3.0f), Is.EqualTo(TalkAction.None));
        Assert.That(gate.Release(3.05f), Is.EqualTo(TalkAction.DropStray));
        Assert.That(gate.Tick(10f), Is.EqualTo(TalkAction.None), "nothing is interrupted later either");
        Assert.That(gate.IsRecording, Is.False);
    }

    [Test]
    public void StrayPressMidTurn_DoesNotInterrupt()
    {
        Ask(0f, 3.5f);

        // Device: a flicker 2 s later while thinking, recorded as 0.22 s including the old grace.
        gate.Press(5.8f, assistantBusy: true);
        Assert.That(gate.Release(5.83f), Is.EqualTo(TalkAction.DropStray));
    }

    [Test]
    public void HeldPressWhileBusy_InterruptsAfterTheHoldTime_ThenRecordsNormally()
    {
        Assert.That(gate.Press(10f, assistantBusy: true), Is.EqualTo(TalkAction.StartPending));
        Assert.That(gate.Tick(10f + TalkGate.InterruptHoldSeconds - 0.01f), Is.EqualTo(TalkAction.None));
        Assert.That(gate.Tick(10f + TalkGate.InterruptHoldSeconds), Is.EqualTo(TalkAction.Interrupt));

        Assert.That(gate.Release(12f), Is.EqualTo(TalkAction.None));
        Assert.That(gate.Tick(12f + TalkGate.ReleaseGraceSeconds), Is.EqualTo(TalkAction.Finish));
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
    public void IdlePress_ListensImmediately()
    {
        Assert.That(gate.Press(0f, assistantBusy: false), Is.EqualTo(TalkAction.Listen));
        Assert.That(gate.Press(0.1f, assistantBusy: false), Is.EqualTo(TalkAction.None), "no double start while recording");
    }

    [Test]
    public void Reset_AllowsANewPress()
    {
        gate.Press(0f, assistantBusy: true);
        gate.Reset();
        Assert.That(gate.Tick(5f), Is.EqualTo(TalkAction.None));
        Assert.That(gate.Press(6f, assistantBusy: false), Is.EqualTo(TalkAction.Listen));
    }
}
