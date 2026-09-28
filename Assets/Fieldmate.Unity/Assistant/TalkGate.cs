namespace Fieldmate.Assistant;

public enum TalkAction
{
    None,

    /// <summary>Idle press: start listening and recording.</summary>
    Listen,

    /// <summary>Press while the assistant is busy: start recording, interrupt only if the press is held.</summary>
    StartPending,

    /// <summary>A busy-time press was held long enough: cancel the running turn and listen.</summary>
    Interrupt,

    /// <summary>A busy-time press was released early: a stray press. Drop the recording, leave the turn alone.</summary>
    DropStray,

    /// <summary>The release grace ran out: the recording is finished.</summary>
    Finish,
}

/// <summary>
/// Push-to-talk timing, kept free of Unity so it can be tested. Hand-tracking pinches flicker in two ways seen on
/// device: a release of a few frames mid-sentence (bridged by <see cref="ReleaseGraceSeconds"/>) and stray presses of
/// a few frames while the assistant works (they must be held <see cref="InterruptHoldSeconds"/> to interrupt).
/// </summary>
public sealed class TalkGate
{
    public const float ReleaseGraceSeconds = 0.25f;
    public const float InterruptHoldSeconds = 0.35f;

    private float releaseAt = -1f;
    private float interruptAt = -1f;
    private bool recording;

    public bool IsRecording => recording;

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
            interruptAt = now + InterruptHoldSeconds;
            return TalkAction.StartPending;
        }

        return TalkAction.Listen;
    }

    public TalkAction Release(float now)
    {
        if (interruptAt >= 0f)
        {
            Reset();
            return TalkAction.DropStray;
        }

        if (recording)
        {
            releaseAt = now + ReleaseGraceSeconds;
        }

        return TalkAction.None;
    }

    public TalkAction Tick(float now)
    {
        if (interruptAt >= 0f && now >= interruptAt)
        {
            interruptAt = -1f;
            return TalkAction.Interrupt;
        }

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
        interruptAt = -1f;
        recording = false;
    }
}
