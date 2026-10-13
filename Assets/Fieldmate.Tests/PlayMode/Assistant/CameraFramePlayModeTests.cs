using System.Collections;
using Fieldmate.Vision;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#20: the bench's camera frame source starts the camera after the permission answer and fails politely.</summary>
public class CameraFramePlayModeTests
{
    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_main_camera_has_a_frame_source_that_starts_the_camera_once_permission_is_answered()
    {
        var source = Object.FindAnyObjectByType<CameraFrameSource>();
        Assert.That(source, Is.Not.Null);
        Assert.That(source.GetComponent<Camera>(), Is.SameAs(Camera.main), "captures from the user's view");
        Assert.That(source.Head, Is.SameAs(Camera.main.transform));

        // Outside Android every permission counts as granted, so the manager is started; the editor has no camera
        // subsystem, so no frame arrives and the source stays Starting.
        Assert.That(source.GetComponent<ARCameraManager>().enabled, Is.True);
        Assert.That(source.State, Is.EqualTo(CameraState.Starting));
        yield break;
    }

    [UnityTest]
    public IEnumerator Without_a_camera_image_a_capture_fails_with_a_reason_and_raises_nothing()
    {
        var source = Object.FindAnyObjectByType<CameraFrameSource>();
        var raised = 0;
        source.Captured += _ => raised++;

        Assert.That(source.TryCapture(out var frame, out var error), Is.False);
        Assert.That(error, Is.EqualTo(CaptureSettings.Explain(CameraState.Starting)));
        Assert.That(frame.Texture, Is.Null);
        Assert.That(raised, Is.Zero, "the privacy indicator only shows for a real capture");
        yield break;
    }

    [UnityTest]
    public IEnumerator A_denied_permission_keeps_passthrough_and_explains_where_to_allow_it()
    {
        var go = new GameObject("Denied Camera", typeof(Camera), typeof(ARCameraManager));
        go.SetActive(false);
        var manager = go.GetComponent<ARCameraManager>();
        var source = go.AddComponent<CameraFrameSource>();
        source.SetUpForTests(manager, go.transform);
        go.SetActive(true);

        Assert.That(manager.enabled, Is.False, "off until the permission is answered");
        source.SimulatePermissionForTests(false);

        Assert.That(manager.enabled, Is.True, "passthrough runs without camera images");
        Assert.That(source.State, Is.EqualTo(CameraState.Denied));
        Assert.That(source.TryCapture(out _, out var error), Is.False);
        StringAssert.Contains("Settings", error);
        Object.Destroy(go);
        yield break;
    }

    [UnityTest]
    public IEnumerator The_default_mount_is_the_quest_left_camera()
    {
        var mount = Object.FindAnyObjectByType<CameraFrameSource>().Mount;

        Assert.That(mount.Offset, Is.EqualTo(CameraMount.Quest3Left.Offset));
        Assert.That(Quaternion.Angle(mount.Rotation, Quaternion.identity), Is.LessThan(0.01f));
        yield break;
    }
}
