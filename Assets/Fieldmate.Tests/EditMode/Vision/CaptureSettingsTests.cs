using System;
using Fieldmate.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Vision;

/// <summary>#20: the capture size is chosen from what the camera supports, downscaled for upload, and rate-limited.</summary>
public class CaptureSettingsTests
{
    [Test]
    public void Picks_the_smallest_configuration_that_still_covers_the_upload_size()
    {
        var sizes = new[] { (1280, 960), (640, 480), (320, 240), (2560, 1920) };

        Assert.That(CaptureSettings.PickConfiguration(sizes), Is.EqualTo(1), "640×480 covers a 640 px upload exactly");
        Assert.That(CaptureSettings.PickConfiguration(sizes, minLongEdge: 1000), Is.EqualTo(0));
    }

    [Test]
    public void Without_a_large_enough_configuration_takes_the_largest()
    {
        Assert.That(CaptureSettings.PickConfiguration(new[] { (320, 240), (480, 360), (0, 0) }), Is.EqualTo(1));
        Assert.That(CaptureSettings.PickConfiguration(Array.Empty<(int, int)>()), Is.EqualTo(-1));
        Assert.That(CaptureSettings.PickConfiguration(new[] { (0, 0) }), Is.EqualTo(-1), "nonsense sizes are skipped");
        Assert.Throws<ArgumentNullException>(() => CaptureSettings.PickConfiguration(null));
    }

    [Test]
    public void Fit_keeps_the_aspect_and_never_upscales()
    {
        Assert.That(CaptureSettings.Fit(1280, 960), Is.EqualTo((640, 480)));
        Assert.That(CaptureSettings.Fit(960, 1280), Is.EqualTo((480, 640)), "portrait");
        Assert.That(CaptureSettings.Fit(1280, 720), Is.EqualTo((640, 360)));
        Assert.That(CaptureSettings.Fit(400, 300), Is.EqualTo((400, 300)), "already small");
        Assert.Throws<ArgumentOutOfRangeException>(() => CaptureSettings.Fit(0, 300));
    }

    [Test]
    public void Captures_are_at_least_a_second_apart()
    {
        Assert.That(CaptureSettings.MayCapture(0.0, double.NegativeInfinity), Is.True, "the first capture");
        Assert.That(CaptureSettings.MayCapture(10.5, 10.0), Is.False);
        Assert.That(CaptureSettings.MayCapture(11.0, 10.0), Is.True);
    }

    [Test]
    public void Every_state_but_ready_has_a_reason_to_give()
    {
        foreach (CameraState state in Enum.GetValues(typeof(CameraState)))
        {
            var reason = CaptureSettings.Explain(state);
            Assert.That(string.IsNullOrEmpty(reason), Is.EqualTo(state == CameraState.Ready), state.ToString());
        }

        StringAssert.Contains("Settings", CaptureSettings.Explain(CameraState.Denied), "denial tells the user where to fix it");
    }

    [Test]
    public void Intrinsics_reported_at_sensor_size_scale_to_the_delivered_image()
    {
        var sensor = new CameraIntrinsics(new Vector2(1727.6f, 1727.6f), new Vector2(1278f, 962f), 2560, 1920);

        var image = sensor.ScaledTo(640, 480);

        Assert.That(image.FocalLength.x, Is.EqualTo(431.9f).Within(1e-3f));
        Assert.That(image.PrincipalPoint.y, Is.EqualTo(240.5f).Within(1e-3f));
        Assert.That(image.HorizontalFov, Is.EqualTo(sensor.HorizontalFov).Within(1e-3f), "same camera, same field of view");
        Assert.That(sensor.ScaledTo(2560, 1920), Is.EqualTo(sensor));
    }
}
