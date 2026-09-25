using Fieldmate.Assistant;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Assistant;

public class PlaybackCursorTests
{
    private const int Clip = 1000;

    [Test]
    public void AllSamplesHandedOver_ButPlayheadBehind_IsNotDrained()
    {
        var cursor = new PlaybackCursor(Clip);
        cursor.OnRead(400, 400); // Unity pre-read the whole utterance; the ring is now empty

        cursor.Advance(150);

        Assert.That(cursor.Drained, Is.False, "the tail is still in Unity's buffer, not yet heard");
    }

    [Test]
    public void PlayheadPastLastRealSample_IsDrained()
    {
        var cursor = new PlaybackCursor(Clip);
        cursor.OnRead(400, 300); // 300 real samples, 100 silence

        cursor.Advance(299);
        Assert.That(cursor.Drained, Is.False);
        cursor.Advance(300);
        Assert.That(cursor.Drained, Is.True);
    }

    [Test]
    public void SilenceFromAnUnderrunMidStream_CountsTowardsTheEnd()
    {
        var cursor = new PlaybackCursor(Clip);
        cursor.OnRead(200, 200);
        cursor.OnRead(200, 0);   // network was slow: silence
        cursor.OnRead(200, 50);  // more speech arrived

        Assert.That(cursor.LastRealEnd, Is.EqualTo(450));
        cursor.Advance(420);
        Assert.That(cursor.Drained, Is.False);
    }

    [Test]
    public void PlayheadWrapsAroundTheLoopingClip()
    {
        var cursor = new PlaybackCursor(Clip);
        cursor.OnRead(1500, 1500);

        cursor.Advance(900);
        cursor.Advance(300); // wrapped: 900 → 1000 → 300

        Assert.That(cursor.Played, Is.EqualTo(1300));
        Assert.That(cursor.Drained, Is.False);
        cursor.Advance(500);
        Assert.That(cursor.Drained, Is.True);
    }

    [Test]
    public void Reset_StartsANewUtterance()
    {
        var cursor = new PlaybackCursor(Clip);
        cursor.OnRead(300, 300);
        cursor.Advance(300);

        cursor.Reset();
        cursor.OnRead(200, 200);

        Assert.That(cursor.Played, Is.Zero);
        Assert.That(cursor.LastRealEnd, Is.EqualTo(200));
        Assert.That(cursor.Drained, Is.False);
    }
}
