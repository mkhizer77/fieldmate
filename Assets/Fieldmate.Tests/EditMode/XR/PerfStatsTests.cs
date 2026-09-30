using Fieldmate.XR;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>#18: the overlay and the logcat line show the budget's numbers in a fixed, grep-able shape.</summary>
public class PerfStatsTests
{
    private static readonly PerfStats Sample = new(13.9f, 12.1f, 0f, 84, 40, 22, 118_500, 412L * 1048576, 18L * 1048576);

    [Test]
    public void Overlay_has_fps_frame_draws_and_memory()
    {
        var text = Sample.Overlay();
        Assert.That(text, Does.StartWith("72 fps · 13.9 ms  (cpu 12.1 / gpu –)"));
        Assert.That(text, Does.Contain("Draws 84 · Batches 40 · SetPass 22 · Tris 118.5k"));
        Assert.That(text, Does.Contain("Mem 412 MB · GC 18 MB"));
    }

    [Test]
    public void Log_line_is_one_grepable_line()
    {
        Assert.That(Sample.LogLine(), Is.EqualTo("[Perf] stats frame 13.9 ms draws 84 batches 40 setpass 22 tris 118500 mem 412 MB gc 18 MB"));
        Assert.That(new PerfStats(0f, 0f, 0f, 0, 0, 0, 0, 0, 0).Fps, Is.EqualTo(0f), "no division by zero before the first window");
    }
}
