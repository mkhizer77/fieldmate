using Fieldmate.AI;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>#69: the hologram's mouth follows the loudness of the speech that is playing.</summary>
public class LipSyncTests
{
    private const float Frame = 1f / 72f;

    private static float Run(LipSync lips, float rms, float seconds)
    {
        for (var t = 0f; t < seconds; t += Frame)
        {
            lips.Update(rms, Frame);
        }

        return lips.Open;
    }

    [Test]
    public void Silence_and_room_noise_keep_the_mouth_shut()
    {
        var lips = new LipSync();
        Assert.That(Run(lips, 0f, 1f), Is.EqualTo(0f));
        Assert.That(Run(lips, 0.005f, 1f), Is.EqualTo(0f), "below the noise floor");
    }

    [Test]
    public void Loud_speech_opens_it_wide_and_quiet_speech_partly()
    {
        Assert.That(Run(new LipSync(), 0.2f, 0.3f), Is.GreaterThan(0.99f));
        var half = Run(new LipSync(), 0.05f, 0.3f);
        Assert.That(half, Is.InRange(0.3f, 0.8f));
    }

    [Test]
    public void It_opens_faster_than_it_closes_and_closes_within_a_quarter_second()
    {
        var lips = new LipSync();
        lips.Update(0.2f, 0.035f);
        var opened = lips.Open;
        var closing = new LipSync();
        Run(closing, 0.2f, 0.5f);
        closing.Update(0f, 0.035f);
        Assert.That(opened, Is.GreaterThan(1f - closing.Open), "one attack step moves more than one release step");
        Assert.That(Run(closing, 0f, 0.25f), Is.LessThan(0.05f));
    }

    [Test]
    public void Syllables_read_as_separate_movements()
    {
        var lips = new LipSync();
        var min = 1f;
        var max = 0f;
        for (var i = 0; i < 8; i++) // ~5 syllables a second: 120 ms loud, 80 ms quiet
        {
            max = System.Math.Max(max, Run(lips, 0.1f, 0.12f));
            min = System.Math.Min(min, Run(lips, 0f, 0.08f));
        }

        Assert.That(max - min, Is.GreaterThan(0.4f));
    }

    [Test]
    public void Close_shuts_at_once_and_rms_is_exact()
    {
        var lips = new LipSync();
        Run(lips, 0.2f, 0.3f);
        lips.Close();
        Assert.That(lips.Open, Is.EqualTo(0f));
        Assert.That(LipSync.Rms(new[] { 0.5f, -0.5f, 0.5f, -0.5f }, 4), Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(LipSync.Rms(null, 4), Is.EqualTo(0f));
        Assert.That(new LipSync().Update(0.2f, 0f), Is.EqualTo(0f), "no time, no movement");
    }
}
