using Fieldmate.Assistant;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Assistant;

public class SpeechDetectorTests
{
    private const float Frame = 1f / 72f;
    private const float Loud = SpeechDetector.Threshold * 2f;
    private const float Quiet = SpeechDetector.Threshold * 0.25f;

    private static bool Feed(SpeechDetector detector, float level, float seconds)
    {
        var detected = false;
        for (var t = 0f; t < seconds; t += Frame)
        {
            detected |= detector.Add(level, Frame);
        }

        return detected;
    }

    [Test]
    public void Silence_IsNeverSpeech()
    {
        Assert.That(Feed(new SpeechDetector(), Quiet, 5f), Is.False);
    }

    [Test]
    public void ABlip_IsNotSpeech()
    {
        Assert.That(Feed(new SpeechDetector(), Loud, 0.1f), Is.False);
    }

    [Test]
    public void SustainedVoice_IsSpeech()
    {
        Assert.That(Feed(new SpeechDetector(), Loud, SpeechDetector.MinVoicedSeconds + 2 * Frame), Is.True);
    }

    [Test]
    public void ShortGapsBetweenSyllables_DoNotReset()
    {
        var detector = new SpeechDetector();
        Feed(detector, Loud, 0.12f);
        Feed(detector, Quiet, 0.05f);
        Assert.That(Feed(detector, Loud, 0.12f), Is.True);
    }

    [Test]
    public void Peak_IsTrackedForTuning_AndReset()
    {
        var detector = new SpeechDetector();
        detector.Add(0.2f, Frame);
        detector.Add(0.05f, Frame);
        Assert.That(detector.Peak, Is.EqualTo(0.2f));

        detector.Reset();
        Assert.That(detector.Peak, Is.Zero);
    }
}
