using Fieldmate.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Vision;

/// <summary>#21: known intrinsics and pose give the expected world ray, and projection inverts it.</summary>
public class PixelToRayTests
{
    // The Quest 3 left camera as measured in the M0 spike (ADR-001): 1280×960, f≈863.8 px, c≈(639, 481).
    private static readonly CameraIntrinsics Quest = new(new Vector2(863.8f, 863.8f), new Vector2(639f, 481f), 1280, 960);
    private static readonly CameraIntrinsics Ideal = new(new Vector2(500f, 500f), new Vector2(320f, 240f), 640, 480);

    private const float Tolerance = 1e-4f;

    private static void AssertDirection(Vector3 actual, Vector3 expected, string what) =>
        Assert.That(Vector3.Angle(actual, expected), Is.LessThan(0.01f), $"{what}: got {actual}, expected {expected.normalized}");

    [Test]
    public void The_principal_point_looks_straight_down_the_camera_axis()
    {
        var ray = PixelToRay.FromPixel(Quest.PrincipalPoint, Quest, new Pose(Vector3.zero, Quaternion.identity));
        AssertDirection(ray.direction, Vector3.forward, "principal point");
    }

    [Test]
    public void Pixels_right_of_and_above_the_centre_point_right_and_up()
    {
        var camera = new Pose(Vector3.zero, Quaternion.identity);

        // 500 px right of centre at f = 500 is 45° to the right; y grows downwards in the image, so a smaller y is up.
        AssertDirection(PixelToRay.FromPixel(new Vector2(820f, 240f), Ideal, camera).direction, new Vector3(1f, 0f, 1f), "right edge");
        AssertDirection(PixelToRay.FromPixel(new Vector2(320f, -260f), Ideal, camera).direction, new Vector3(0f, 1f, 1f), "above");
        AssertDirection(PixelToRay.FromPixel(new Vector2(70f, 490f), Ideal, camera).direction, new Vector3(-0.5f, -0.5f, 1f), "lower left");
    }

    [Test]
    public void The_ray_starts_at_the_camera_and_turns_with_it()
    {
        var camera = new Pose(new Vector3(1f, 1.6f, -2f), Quaternion.Euler(0f, 90f, 0f));

        var ray = PixelToRay.FromPixel(Ideal.PrincipalPoint, Ideal, camera);

        Assert.That(Vector3.Distance(ray.origin, camera.position), Is.LessThan(Tolerance));
        AssertDirection(ray.direction, Vector3.right, "camera turned 90° right looks along +x");
    }

    [Test]
    public void Normalized_box_coordinates_map_onto_the_capture_resolution()
    {
        var camera = new Pose(Vector3.zero, Quaternion.identity);

        var fromNormalized = PixelToRay.FromNormalized(new Vector2(0.25f, 0.75f), Quest, camera);
        var fromPixel = PixelToRay.FromPixel(new Vector2(320f, 720f), Quest, camera);

        AssertDirection(fromNormalized.direction, fromPixel.direction, "the same point in both conventions");
    }

    [Test]
    public void Projecting_a_point_on_a_pixel_ray_gives_that_pixel_back()
    {
        var camera = new Pose(new Vector3(0.3f, 1.5f, 0.2f), Quaternion.Euler(12f, -35f, 4f));
        foreach (var pixel in new[] { new Vector2(10f, 10f), new Vector2(639f, 481f), new Vector2(1200f, 900f), new Vector2(900f, 100f) })
        {
            var ray = PixelToRay.FromPixel(pixel, Quest, camera);

            Assert.That(PixelToRay.TryToPixel(ray.GetPoint(2.4f), Quest, camera, out var back), Is.True);
            Assert.That(Vector2.Distance(back, pixel), Is.LessThan(0.01f), $"round trip of {pixel}");
        }
    }

    [Test]
    public void A_point_behind_the_camera_has_no_pixel()
    {
        var camera = new Pose(Vector3.zero, Quaternion.identity);

        Assert.That(PixelToRay.TryToPixel(new Vector3(0f, 0f, -1f), Quest, camera, out _), Is.False);
        Assert.That(PixelToRay.TryToPixel(new Vector3(0.5f, 0f, 0f), Quest, camera, out _), Is.False, "in the camera plane");
    }

    [Test]
    public void The_gaze_pixel_accounts_for_the_camera_sitting_off_the_eyes()
    {
        var head = new Pose(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
        var mount = CameraMount.Quest3Left;
        var camera = mount.CameraPose(head);
        var gaze = new Ray(head.position, Vector3.forward);

        Assert.That(PixelToRay.TryGazePixel(gaze, 1f, Quest, camera, out var near), Is.True);
        Assert.That(PixelToRay.TryGazePixel(gaze, 10f, Quest, camera, out var far), Is.True);

        // The camera is left of and below the eyes, so what the user looks at appears right of and above the image
        // centre, more so the closer it is.
        Assert.That(near.x, Is.GreaterThan(far.x));
        Assert.That(far.x, Is.GreaterThan(Quest.PrincipalPoint.x));
        Assert.That(near.y, Is.LessThan(far.y));
        Assert.That(near.x - Quest.PrincipalPoint.x, Is.EqualTo(863.8f * 0.032f / 0.94f).Within(0.5f), "parallax at 1 m");
    }

    [Test]
    public void A_gaze_point_outside_the_frame_is_rejected()
    {
        var camera = new Pose(Vector3.zero, Quaternion.identity);
        var gaze = new Ray(Vector3.zero, new Vector3(1f, 0f, 0.2f)); // 79° to the right; the camera sees ±37°

        Assert.That(PixelToRay.TryGazePixel(gaze, 2f, Quest, camera, out _), Is.False);
    }

    [Test]
    public void The_camera_mount_offsets_in_head_space()
    {
        var head = new Pose(new Vector3(1f, 1.6f, 0f), Quaternion.Euler(0f, 90f, 0f));
        var mount = new CameraMount(new Vector3(-0.03f, -0.02f, 0.06f), Quaternion.Euler(10f, 0f, 0f));

        var camera = mount.CameraPose(head);

        // Head turned to +x: its left (-x local) is world +z, its forward (+z local) is world +x.
        Assert.That(Vector3.Distance(camera.position, new Vector3(1.06f, 1.58f, 0.03f)), Is.LessThan(Tolerance));
        AssertDirection(camera.rotation * Vector3.forward, Quaternion.Euler(10f, 90f, 0f) * Vector3.forward, "pitched 10° down");
        Assert.That(new CameraMount().CameraPose(head).rotation, Is.EqualTo(head.rotation), "a default mount is the head");
    }

    [Test]
    public void Intrinsics_reject_nonsense_and_report_the_field_of_view()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new CameraIntrinsics(Vector2.zero, Vector2.zero, 640, 480));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new CameraIntrinsics(Vector2.one, Vector2.zero, 0, 480));
        Assert.That(default(CameraIntrinsics).IsValid, Is.False);
        Assert.That(Ideal.HorizontalFov, Is.EqualTo(2f * Mathf.Atan(0.64f) * Mathf.Rad2Deg).Within(1e-3f));
        Assert.That(Quest.HorizontalFov, Is.EqualTo(73.1f).Within(0.2f));
    }
}
