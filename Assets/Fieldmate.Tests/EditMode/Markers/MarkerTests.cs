using System;
using System.Linq;
using Fieldmate.Markers;
using Fieldmate.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Markers;

/// <summary>
/// #27: machine tags are found, identified and located in synthetic camera frames rendered with a known pose, the same
/// 640×480 the headset sends through the pipeline.
/// </summary>
public class MarkerTests
{
    private static readonly CameraIntrinsics Camera = new(new Vector2(432f, 432f), new Vector2(320f, 240f), 640, 480);
    private const float Side = 0.2f;

    /// <summary>Renders tag <paramref name="id"/> (with its white margin) at <paramref name="pose"/> in camera space.</summary>
    internal static byte[] Render(int id, Pose pose, float side = Side, int noise = 6, int seed = 1) =>
        RenderAll(new[] { (id, pose) }, side, noise, seed);

    /// <summary>Renders several tags over a checkered "room", 2×2 supersampled, with a little sensor noise.</summary>
    internal static byte[] RenderAll((int id, Pose pose)[] tags, float side = Side, int noise = 6, int seed = 1)
    {
        var w = Camera.Width;
        var h = Camera.Height;
        var image = new byte[w * h];
        var random = new System.Random(seed);
        const int n = MarkerDictionary.GridCells;
        for (var v = 0; v < h; v++)
        {
            for (var u = 0; u < w; u++)
            {
                var sum = 0f;
                for (var s = 0; s < 4; s++)
                {
                    var px = u + 0.25f + 0.5f * (s % 2);
                    var py = v + 0.25f + 0.5f * (s / 2);
                    var d = new Vector3((px - Camera.PrincipalPoint.x) / Camera.FocalLength.x, -(py - Camera.PrincipalPoint.y) / Camera.FocalLength.y, 1f);
                    var value = 120f + (((u / 23) + (v / 17)) % 2 == 0 ? 20f : -20f);
                    var nearest = float.MaxValue;
                    foreach (var (id, pose) in tags)
                    {
                        var normal = pose.rotation * Vector3.forward;
                        var denom = Vector3.Dot(d, normal);
                        if (Mathf.Abs(denom) < 1e-6f)
                        {
                            continue;
                        }

                        var t = Vector3.Dot(pose.position, normal) / denom;
                        if (t <= 0f || t >= nearest)
                        {
                            continue;
                        }

                        var local = Quaternion.Inverse(pose.rotation) * (d * t - pose.position);
                        var cx = (local.x + side / 2f) / side * n;
                        var cy = (side / 2f - local.y) / side * n;
                        if (cx < -1f || cy < -1f || cx >= n + 1f || cy >= n + 1f)
                        {
                            continue;
                        }

                        nearest = t;
                        var column = Mathf.FloorToInt(cx);
                        var row = Mathf.FloorToInt(cy);
                        var black = column >= 0 && row >= 0 && column < n && row < n &&
                                    (column == 0 || row == 0 || column == n - 1 || row == n - 1 || MarkerDictionary.Cell(id, row - 1, column - 1));
                        value = black ? 25f : 225f;
                    }

                    sum += value;
                }

                image[v * w + u] = (byte)Mathf.Clamp(sum / 4f + random.Next(-noise, noise + 1), 0f, 255f);
            }
        }

        return image;
    }

    private static Vector2 Project(Vector3 p) =>
        new(Camera.PrincipalPoint.x + Camera.FocalLength.x * p.x / p.z, Camera.PrincipalPoint.y - Camera.FocalLength.y * p.y / p.z);

    private static Vector2[] TrueCorners(Pose pose, float side = Side)
    {
        var half = side / 2f;
        return new[] { new Vector3(-half, half), new Vector3(half, half), new Vector3(half, -half), new Vector3(-half, -half) }
            .Select(c => Project(pose.position + pose.rotation * c)).ToArray();
    }

    [Test]
    public void The_dictionary_has_well_separated_codes_in_every_rotation()
    {
        for (var i = 0; i < MarkerDictionary.Count; i++)
        {
            var code = MarkerDictionary.Code(i);
            var turned = code;
            for (var r = 1; r < 4; r++)
            {
                turned = MarkerDictionary.RotateCounterClockwise(turned);
                Assert.That(MarkerDictionary.Distance(code, turned), Is.GreaterThanOrEqualTo(MarkerDictionary.MinDistance), $"tag {i} vs its own rotation {r}");
            }

            for (var j = i + 1; j < MarkerDictionary.Count; j++)
            {
                var other = MarkerDictionary.Code(j);
                for (var r = 0; r < 4; r++)
                {
                    Assert.That(MarkerDictionary.Distance(code, other), Is.GreaterThanOrEqualTo(MarkerDictionary.MinDistance), $"tags {i} and {j}");
                    other = MarkerDictionary.RotateCounterClockwise(other);
                }
            }
        }
    }

    [Test]
    public void The_dictionary_is_fixed_forever()
    {
        // Printed tags depend on these; a change here breaks every tag in the field.
        var fingerprint = Enumerable.Range(0, MarkerDictionary.Count).Aggregate(17L, (acc, i) => acc * 31 + MarkerDictionary.Code(i));
        Assert.That(fingerprint, Is.EqualTo(ExpectedFingerprint), "the dictionary changed");
    }

    // The generated dictionary as first released; see The_dictionary_is_fixed_forever.
    private const long ExpectedFingerprint = -6933194786157794049;

    [Test]
    public void Two_wrong_cells_still_read_three_do_not()
    {
        var code = MarkerDictionary.Code(5);
        var two = code ^ MarkerDictionary.Mask(0, 0) ^ MarkerDictionary.Mask(4, 3);
        Assert.That(MarkerDictionary.TryMatch(two, out var id, out var rotation, out var errors), Is.True);
        Assert.That((id, rotation, errors), Is.EqualTo((5, 0, 2)));

        var turned = MarkerDictionary.RotateCounterClockwise(code); // the tag seen turned a quarter counter-clockwise
        Assert.That(MarkerDictionary.TryMatch(turned, out id, out rotation, out _), Is.True);
        Assert.That((id, rotation), Is.EqualTo((5, 3)), "three quarter turns clockwise undo one counter-clockwise");
    }

    [Test]
    public void Finds_a_tag_straight_ahead_and_its_corners()
    {
        var pose = new Pose(new Vector3(0.05f, -0.03f, 0.8f), Quaternion.identity);

        var found = MarkerDetector.Detect(Render(7, pose), Camera.Width, Camera.Height);

        Assert.That(found.Select(f => f.Id), Is.EqualTo(new[] { 7 }));
        var truth = TrueCorners(pose);
        for (var k = 0; k < 4; k++)
        {
            Assert.That(Vector2.Distance(found[0].Corners[k], truth[k]), Is.LessThan(1.5f), $"corner {k}");
        }
    }

    [TestCase(0, 0f, 0f, 0f)]
    [TestCase(3, 90f, 0f, 0f)]
    [TestCase(12, 180f, 30f, 0f)]
    [TestCase(21, -90f, 0f, 35f)]
    [TestCase(31, 20f, -40f, 15f)]
    public void Finds_tags_turned_and_tilted_and_recovers_their_pose(int id, float roll, float pitch, float yaw)
    {
        var pose = new Pose(new Vector3(-0.08f, 0.04f, 0.9f), Quaternion.Euler(pitch, yaw, roll));

        var found = MarkerDetector.Detect(Render(id, pose, seed: id), Camera.Width, Camera.Height);

        Assert.That(found.Select(f => f.Id), Is.EqualTo(new[] { id }));
        var truth = TrueCorners(pose);
        for (var k = 0; k < 4; k++)
        {
            Assert.That(Vector2.Distance(found[0].Corners[k], truth[k]), Is.LessThan(2f), $"corner {k} keeps the tag's own orientation");
        }

        Assert.That(Vector2.Distance(found[0].ProjectiveCenter, Project(pose.position)), Is.LessThan(1.5f), "the diagonals cross at the true centre");
        Assert.That(MarkerPose.TryEstimate(found[0].Corners, Side, Camera, out var estimate), Is.True);
        Assert.That(Vector3.Distance(estimate.position, pose.position), Is.LessThan(0.015f), "within 1.5 cm at 0.9 m");
        Assert.That(Quaternion.Angle(estimate.rotation, pose.rotation), Is.LessThan(4f));
    }

    [Test]
    public void A_floor_tag_seen_from_standing_height_is_found()
    {
        // 1.3 m below the eyes, 1.2 m ahead, lying flat (its face up, its top edge pointing away from the viewer).
        var camera = Quaternion.Euler(45f, 0f, 0f); // looking down 45°
        var tagWorld = new Pose(new Vector3(0f, -1.3f, 1.2f), Quaternion.Euler(90f, 0f, 0f));
        var inCamera = new Pose(Quaternion.Inverse(camera) * tagWorld.position, Quaternion.Inverse(camera) * tagWorld.rotation);

        var found = MarkerDetector.Detect(Render(9, inCamera), Camera.Width, Camera.Height);

        Assert.That(found.Select(f => f.Id), Is.EqualTo(new[] { 9 }));
        Assert.That(MarkerPose.TryEstimate(found[0].Corners, Side, Camera, out var estimate), Is.True);
        var world = MarkerPose.ToWorld(estimate, new Pose(Vector3.zero, camera));
        Assert.That(Vector3.Distance(world.position, tagWorld.position), Is.LessThan(0.03f));
        Assert.That(Vector3.Angle(world.rotation * Vector3.forward, Vector3.down), Is.LessThan(5f), "into the floor");
        Assert.That(Vector3.Angle(world.rotation * Vector3.up, Vector3.forward), Is.LessThan(5f), "the tag's top points away");
    }

    [Test]
    public void Finds_two_tags_at_once()
    {
        var both = RenderAll(new[] { (1, new Pose(new Vector3(-0.2f, 0f, 1f), Quaternion.identity)), (2, new Pose(new Vector3(0.2f, 0f, 1f), Quaternion.identity)) }, side: 0.15f);
        var ids = MarkerDetector.Detect(both, Camera.Width, Camera.Height).Select(f => f.Id).OrderBy(i => i);
        Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void Noise_and_an_empty_room_give_nothing()
    {
        var random = new System.Random(3);
        var noise = new byte[Camera.Width * Camera.Height];
        random.NextBytes(noise);
        Assert.That(MarkerDetector.Detect(noise, Camera.Width, Camera.Height), Is.Empty);

        var room = Render(0, new Pose(new Vector3(0f, 0f, -1f), Quaternion.identity)); // behind the camera: just the room
        Assert.That(MarkerDetector.Detect(room, Camera.Width, Camera.Height), Is.Empty);
    }

    [Test]
    public void A_tag_too_far_away_or_cut_off_is_not_reported()
    {
        Assert.That(MarkerDetector.Detect(Render(4, new Pose(new Vector3(0f, 0f, 6f), Quaternion.identity)), Camera.Width, Camera.Height), Is.Empty, "a few pixels wide");
        Assert.That(MarkerDetector.Detect(Render(4, new Pose(new Vector3(0.62f, 0f, 0.8f), Quaternion.identity)), Camera.Width, Camera.Height), Is.Empty, "half outside the frame");
    }

    [Test]
    public void The_printable_image_reads_back_as_its_tag()
    {
        var tag = MarkerImage.Render(7, 20, out var size);
        Assert.That(size, Is.EqualTo(180));

        // Paste it into a mid-gray frame, as if photographed square-on.
        var frame = Enumerable.Repeat((byte)128, 640 * 480).ToArray();
        for (var y = 0; y < size; y++)
        {
            Array.Copy(tag, y * size, frame, (150 + y) * 640 + 230, size);
        }

        var found = MarkerDetector.Detect(frame, 640, 480);
        Assert.That(found.Select(f => f.Id), Is.EqualTo(new[] { 7 }));
        Assert.That(found[0].Errors, Is.Zero);
        Assert.That(Vector2.Distance(found[0].Corners[0], new Vector2(250f, 170f)), Is.LessThan(1f), "top-left of the black square");
    }

    [Test]
    public void A_floor_tag_puts_the_machine_on_it_facing_the_reader()
    {
        // Tag on the floor, its top edge pointing +x: the reader stood on its -x side.
        var tag = new Pose(new Vector3(2f, 0.004f, 1f), Quaternion.LookRotation(Vector3.down, Vector3.right));

        var machine = TagMount.MachinePose(tag, TagMountKind.Floor, Vector3.zero, 0f, 0.03f);

        Assert.That(Vector3.Distance(machine.position, new Vector3(2f, 0.034f, 1f)), Is.LessThan(1e-4f));
        Assert.That(Vector3.Angle(machine.rotation * Vector3.forward, Vector3.left), Is.LessThan(0.01f), "front towards the reader");
        Assert.That(Vector3.Angle(machine.rotation * Vector3.up, Vector3.up), Is.LessThan(0.01f), "upright");

        var offset = TagMount.MachinePose(tag, TagMountKind.Floor, new Vector3(0f, 0f, -0.5f), 90f, 0.03f);
        Assert.That(Vector3.Angle(offset.rotation * Vector3.forward, Vector3.forward), Is.LessThan(0.01f), "yaw turns it");
    }

    [Test]
    public void A_wall_tag_faces_the_machine_out_of_its_surface_even_when_read_tilted()
    {
        // A nameplate on a face looking -z, read with the head tilted 10°.
        var tag = new Pose(new Vector3(0f, 1.2f, 3f), Quaternion.LookRotation(Vector3.forward, Vector3.up) * Quaternion.Euler(10f, 0f, 5f));

        var machine = TagMount.MachinePose(tag, TagMountKind.Wall, new Vector3(0f, -1.2f, -0.4f), 0f, 0f);

        Assert.That(Vector3.Angle(machine.rotation * Vector3.forward, Vector3.back), Is.LessThan(0.01f));
        Assert.That(Vector3.Angle(machine.rotation * Vector3.up, Vector3.up), Is.LessThan(0.01f));
        Assert.That(machine.position.y, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(machine.position.z, Is.EqualTo(3.4f).Within(1e-4f), "behind the nameplate");
    }

    [Test]
    public void Luminance_flips_texture_rows_upright()
    {
        // A 1×2 RGBA texture: bottom row white, top row black (textures store the bottom row first).
        var rgba = new byte[] { 255, 255, 255, 255, 0, 0, 0, 255 };
        var gray = MarkerDetector.Luminance(rgba, 1, 2);
        Assert.That(gray[0], Is.EqualTo(0), "top row first");
        Assert.That(gray[1], Is.GreaterThan(250));
    }

    [Test]
    public void Pose_needs_four_corners_a_size_and_intrinsics()
    {
        Assert.That(MarkerPose.TryEstimate(new Vector2[3], Side, Camera, out _), Is.False);
        Assert.That(MarkerPose.TryEstimate(TrueCorners(new Pose(Vector3.forward, Quaternion.identity)), 0f, Camera, out _), Is.False);
        Assert.That(MarkerPose.TryEstimate(TrueCorners(new Pose(Vector3.forward, Quaternion.identity)), Side, default, out _), Is.False);
        Assert.That(MarkerPose.TryEstimate(new[] { Vector2.zero, Vector2.one, Vector2.one * 2, Vector2.one * 3 }, Side, Camera, out _), Is.False, "collinear");
    }
}
