using Fieldmate.Assistant;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Assistant;

public class PlaybackCursorTests
{
    [Test]
    public void LastRealEnd_IsThePositionAfterTheLastRealSample()
    {
        var cursor = new PlaybackCursor();
        cursor.OnRead(400, 300); // 300 real samples, then silence

        Assert.That(cursor.LastRealEnd, Is.EqualTo(300));
        Assert.That(cursor.LastRealEndSeconds(100), Is.EqualTo(3.0).Within(1e-9));
    }

    [Test]
    public void SilenceFromAnUnderrunMidStream_CountsTowardsTheEnd()
    {
        var cursor = new PlaybackCursor();
        cursor.OnRead(200, 200);
        cursor.OnRead(200, 0);   // network was slow: silence is played too
        cursor.OnRead(200, 50);  // more speech arrived

        Assert.That(cursor.LastRealEnd, Is.EqualTo(450));
    }

    [Test]
    public void TrailingSilenceReads_DoNotMoveTheEnd()
    {
        var cursor = new PlaybackCursor();
        cursor.OnRead(1000, 1000);
        cursor.OnRead(1000, 0);
        cursor.OnRead(1000, 0); // Unity keeps reading ahead after the speech ended

        Assert.That(cursor.LastRealEnd, Is.EqualTo(1000));
    }

    [Test]
    public void Reset_StartsANewUtterance()
    {
        var cursor = new PlaybackCursor();
        cursor.OnRead(300, 300);

        cursor.Reset();
        cursor.OnRead(200, 200);

        Assert.That(cursor.LastRealEnd, Is.EqualTo(200));
    }

    [Test]
    public void UnknownSampleRate_IsZeroSeconds()
    {
        Assert.That(new PlaybackCursor().LastRealEndSeconds(0), Is.Zero);
    }
}
