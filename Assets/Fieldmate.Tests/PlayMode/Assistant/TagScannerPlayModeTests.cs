using System.Collections;
using System.Threading.Tasks;
using Fieldmate.Assistant;
using Fieldmate.Markers;
using Fieldmate.Twin;
using Fieldmate.Vision;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>
/// #27 in the bench: a camera frame showing the skid's tag on the floor (rendered from the head's pose; the editor has
/// no passthrough camera) makes the scanner look the machine up through Addressables and stand it on the tag.
/// </summary>
public class TagScannerPlayModeTests
{
    private static readonly CameraIntrinsics Intrinsics = new(new Vector2(432f, 432f), new Vector2(320f, 240f), 640, 480);

    private TagScanner scanner;
    private CameraFrameSource source;
    private Transform head;
    private Transform skid;
    private Texture2D frame;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        PlayerPrefs.DeleteKey(PrivacySettings.PrefsKey);
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        yield return ProcedurePlayModeTests.PlaceMachine();
        scanner = Object.FindAnyObjectByType<TagScanner>();
        source = Object.FindAnyObjectByType<CameraFrameSource>();
        head = Camera.main.transform;
        skid = Object.FindAnyObjectByType<MachineServices>().Parts["relief_valve"].transform.root;
        head.SetPositionAndRotation(new Vector3(0.3f, 1.6f, -2f), Quaternion.Euler(45f, 20f, 0f));
        frame = new Texture2D(640, 480, TextureFormat.RGBA32, false);
    }

    [TearDown]
    public void Clean()
    {
        PrivacySettings.LocalOnly = false;
        Object.Destroy(frame);
    }

    private void ShowFrame(int tagId, Pose tagWorld)
    {
        var camera = new Pose(head.position, head.rotation);
        var inCamera = new Pose(Quaternion.Inverse(camera.rotation) * (tagWorld.position - camera.position), Quaternion.Inverse(camera.rotation) * tagWorld.rotation);
        Paint(frame, tagId, inCamera, tagId >= 0);
        source.CaptureForTests = (out CameraFrame f, out string error) =>
        {
            f = new CameraFrame(frame, Intrinsics, camera, camera, 0d, 1);
            error = null;
            return true;
        };
    }

    // Draws the tag (20 cm, with its white margin) over a plain floor, as the left camera would see it.
    private static void Paint(Texture2D texture, int id, Pose pose, bool drawTag)
    {
        const int n = MarkerDictionary.GridCells;
        const float side = 0.2f;
        var pixels = new Color32[640 * 480];
        var normal = pose.rotation * Vector3.forward;
        var inverse = Quaternion.Inverse(pose.rotation);
        for (var v = 0; v < 480; v++)
        {
            for (var u = 0; u < 640; u++)
            {
                var d = new Vector3((u + 0.5f - 320f) / 432f, -(v + 0.5f - 240f) / 432f, 1f);
                byte g = 110;
                var denom = Vector3.Dot(d, normal);
                if (drawTag && Mathf.Abs(denom) > 1e-6f)
                {
                    var t = Vector3.Dot(pose.position, normal) / denom;
                    var local = inverse * (d * t - pose.position);
                    var cx = (local.x + side / 2f) / side * n;
                    var cy = (side / 2f - local.y) / side * n;
                    if (t > 0f && cx >= -1f && cy >= -1f && cx < n + 1f && cy < n + 1f)
                    {
                        int column = Mathf.FloorToInt(cx), row = Mathf.FloorToInt(cy);
                        var black = column >= 0 && row >= 0 && column < n && row < n &&
                                    (column == 0 || row == 0 || column == n - 1 || row == n - 1 || MarkerDictionary.Cell(id, row - 1, column - 1));
                        g = black ? (byte)20 : (byte)230;
                    }
                }

                pixels[(479 - v) * 640 + u] = new Color32(g, g, g, 255); // texture rows bottom-up
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
    }

    private static IEnumerator Await<T>(Task<T> task, System.Action<T> result)
    {
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted) throw task.Exception!.GetBaseException();
        result(task.Result);
    }

    [UnityTest]
    public IEnumerator Scanning_the_skids_floor_tag_stands_the_machine_on_it()
    {
        // On the floor 1.4 m ahead, its top edge pointing away from the user (read upright from where they stand).
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        var tag = new Pose(new Vector3(head.position.x, 0f, head.position.z) + forward * 1.4f, Quaternion.LookRotation(Vector3.down, forward));
        ShowFrame(MachineTagsSetupTag, tag);

        TagScanner.Outcome outcome = default;
        yield return Await(scanner.ScanAsync(), o => outcome = o);

        Assert.That(outcome, Is.EqualTo(TagScanner.Outcome.Placed), scanner.Last.message);
        Assert.That(Vector3.Angle(skid.forward, -forward), Is.LessThan(3f), "the machine faces the user who read the tag");
        var expected = tag.position + skid.rotation * new Vector3(0f, 0f, -0.9f);
        Assert.That(Vector3.Distance(new Vector3(skid.position.x, 0f, skid.position.z), new Vector3(expected.x, 0f, expected.z)), Is.LessThan(0.05f),
            "stands just behind its tag");
        Assert.That(skid.position.y, Is.EqualTo(PlacementMath.FloorClearance).Within(0.02f));
        Assert.That(Object.FindAnyObjectByType<MachinePlacement>().State, Is.EqualTo(PlacementState.Placed));
        StringAssert.Contains("on its tag", scanner.Last.message);
    }

    // The pump skid's tag id (Fieldmate.Editor.MachineTagsSetup.PumpSkidTag; the editor assembly isn't referenced here).
    private const int MachineTagsSetupTag = 7;

    [UnityTest]
    public IEnumerator A_frame_without_a_tag_or_with_a_strangers_tag_moves_nothing()
    {
        var before = skid.position;
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        var tag = new Pose(new Vector3(head.position.x, 0f, head.position.z) + forward * 1.4f, Quaternion.LookRotation(Vector3.down, forward));

        ShowFrame(-1, tag);
        TagScanner.Outcome outcome = default;
        yield return Await(scanner.ScanAsync(), o => outcome = o);
        Assert.That(outcome, Is.EqualTo(TagScanner.Outcome.NoTag));

        yield return new WaitForSecondsRealtime(1.1f); // one capture a second
        ShowFrame(3, tag);
        yield return Await(scanner.ScanAsync(), o => outcome = o);
        Assert.That(outcome, Is.EqualTo(TagScanner.Outcome.UnknownMachine), "tag 3 has no Addressables entry");
        StringAssert.Contains("Tag 3", scanner.Last.message);
        Assert.That(skid.position, Is.EqualTo(before));
    }

    [UnityTest]
    public IEnumerator Local_only_mode_keeps_the_camera_off_for_tags_too()
    {
        PrivacySettings.LocalOnly = true;
        ShowFrame(MachineTagsSetupTag, new Pose(Vector3.zero, Quaternion.identity));
        var captures = 0;
        source.Captured += _ => captures++;

        TagScanner.Outcome outcome = default;
        yield return Await(scanner.ScanAsync(), o => outcome = o);

        Assert.That(outcome, Is.EqualTo(TagScanner.Outcome.CameraUnavailable));
        Assert.That(captures, Is.Zero);
        StringAssert.Contains("Local-only", scanner.Last.message);
    }

    [UnityTest]
    public IEnumerator The_machine_definition_loads_by_tag_id_through_addressables()
    {
        var task = MachineCatalog.LoadAsync(MachineTagsSetupTag);
        MachineDefinition definition = null;
        yield return Await(task, d => definition = d);

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.TagId, Is.EqualTo(MachineTagsSetupTag));
        Assert.That(definition.MachineName, Is.EqualTo(Object.FindAnyObjectByType<MachineServices>().Manual.MachineName));
        Assert.That(definition.Manual, Is.Not.Null);
    }
}
