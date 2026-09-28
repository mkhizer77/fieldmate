namespace Fieldmate.Assistant;

public enum TalkAction
{
    None,

    /// <summary>Idle press: start listening and recording.</summary>
    Listen,

    /// <summary>Press while the assistant is busy: record, but leave the reply running until the user speaks.</summary>
    StartPending,

    /// <summary>Speech was heard during a pending press: stop the reply and listen.</summary>
    Interrupt,

    /// <summary>A pending press ended without speech and too short to hold any: drop it, the reply continues.</summary>
    DropStray,

    /// <summary>A pending press ended without detected speech but long enough to hold some: let speech-to-text decide.</summary>
    CheckSpeech,

    /// <summary>The release grace ran out: the recording is finished.</summary>
    Finish,
}

/// <summary>
/// Push-to-talk decisions, kept free of Unity so they can be tested. Pressing and speaking are separate: a press only
/// opens the microphone; while the assistant is busy, its reply is interrupted only once speech is heard
/// (<see cref="SpeechDetected"/>). Hand-tracking pinches flicker, so a release shorter than
/// <see cref="ReleaseGraceSeconds"/> is bridged, and pending presses shorter than <see cref="MinSpeechSeconds"/> are
/// dropped without interrupting anything.
/// </summary>
public sealed class TalkGate
{
    public const float ReleaseGraceSeconds = 0.25f;

    /// <summary>Shorter recordings can't hold a question (seen on device: 0.1–0.4 s pinch flickers).</summary>
    public const float MinSpeechSeconds = 0.6f;

    private float releaseAt = -1f;
    private float pendingSince = -1f;
    private bool recording;

    public bool IsRecording => recording;

    /// <summary>A press while busy is open and no speech has been heard yet.</summary>
    public bool IsPending => pendingSince >= 0f;

    public TalkAction Press(float now, bool assistantBusy)
    {
        if (releaseAt >= 0f)
        {
            releaseAt = -1f; // re-pressed within the grace period: keep recording
            return TalkAction.None;
        }

        if (recording)
        {
            return TalkAction.None;
        }

        recording = true;
        if (assistantBusy)
        {
            pendingSince = now;
            return TalkAction.StartPending;
        }

        return TalkAction.Listen;
    }

    /// <summary>The microphone picked up speech.</summary>
    public TalkAction SpeechDetected()
    {
        if (!IsPending)
        {
            return TalkAction.None;
        }

        pendingSince = -1f;
        return TalkAction.Interrupt;
    }

    public TalkAction Release(float now)
    {
        if (IsPending)
        {
            var held = now - pendingSince;
            Reset();
            return held >= MinSpeechSeconds ? TalkAction.CheckSpeech : TalkAction.DropStray;
        }

        if (recording)
        {
            releaseAt = now + ReleaseGraceSeconds;
        }

        return TalkAction.None;
    }

    public TalkAction Tick(float now)
    {
        if (releaseAt >= 0f && now >= releaseAt)
        {
            Reset();
            return TalkAction.Finish;
        }

        return TalkAction.None;
    }

    /// <summary>Recording could not start or was abandoned.</summary>
    public void Reset()
    {
        releaseAt = -1f;
        pendingSince = -1f;
        recording = false;
    }
}
