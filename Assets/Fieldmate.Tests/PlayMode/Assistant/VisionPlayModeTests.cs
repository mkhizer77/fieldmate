using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.Procedures;
using Fieldmate.Vision;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>
/// #22: "What's this?" end to end in the bench. The editor has no passthrough camera, so a frame of the right shape
/// stands in for it; everything after the capture (gaze, candidates, prompt, JPEG, parsing, fusion, label) is real.
/// </summary>
public class VisionPlayModeTests
{
    private static readonly CameraIntrinsics Intrinsics = new(new Vector2(500f, 500f), new Vector2(320f, 240f), 640, 480);

    private VisionRequester requester;
    private MachineServices machine;
    private Transform head;
    private Texture2D image;
    private FakeVision model;
    private Vector3 valveCentre;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        PlayerPrefs.DeleteKey(PrivacySettings.PrefsKey);
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        yield return ProcedurePlayModeTests.PlaceMachine();
        requester = UnityEngine.Object.FindAnyObjectByType<VisionRequester>();
        machine = UnityEngine.Object.FindAnyObjectByType<MachineServices>();
        head = Camera.main.transform;

        // Stand 1 m in front of the relief valve and look at it (the skid's front is +Z).
        var valve = machine.Parts["relief_valve"];
        var renderers = valve.GetComponentsInChildren<MeshRenderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        valveCentre = bounds.center;
        var skid = valve.transform.root;
        head.position = valveCentre + skid.forward * 1f;
        head.rotation = Quaternion.LookRotation(valveCentre - head.position, Vector3.up);
        Physics.SyncTransforms();

        image = new Texture2D(640, 480, TextureFormat.RGBA32, false);
        model = new FakeVision();
        requester.SetModel(model);
        requester.FrameSource.CaptureForTests = Capture;
    }

    [TearDown]
    public void Clean()
    {
        PrivacySettings.LocalOnly = false;
        if (image != null)
        {
            UnityEngine.Object.Destroy(image);
        }
    }

    private bool Capture(out CameraFrame frame, out string error)
    {
        var pose = new Pose(head.position, head.rotation);
        frame = new CameraFrame(image, Intrinsics, pose, pose, 0d, 1);
        error = null;
        return true;
    }

    private static IEnumerator Await<T>(Task<T> task, Action<T> result)
    {
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted) throw task.Exception!.GetBaseException();
        result(task.Result);
    }

    private string ValveName => machine.Catalog.DisplayName("relief_valve");

    [UnityTest]
    public IEnumerator A_confident_answer_is_pinned_on_the_part_the_box_points_at()
    {
        Assert.That(machine.PartAlong(new Ray(head.position, head.forward))?.Id, Is.EqualTo("relief_valve"), "test setup: gaze on the valve");
        model.Reply = "{\"label\":\"Relief valve\",\"part_id\":\"relief_valve\",\"confidence\":0.9,\"bbox\":[0.45,0.42,0.55,0.58],\"one_line_help\":\"Vents the line above 6 bar.\"}";
        FusedAnswer answered = null;
        requester.Answered += a => answered = a;

        ViewAnswer result = default;
        yield return Await(requester.IdentifyAsync("en", CancellationToken.None), r => result = r);

        Assert.That(result.Identified, Is.True);
        Assert.That(result.Text, Does.StartWith($"That's the {ValveName}."));
        Assert.That(answered.Source, Is.EqualTo(AnswerSource.Vision));
        Assert.That(requester.Labels.TitleFor("relief_valve"), Is.EqualTo(ValveName));
        Assert.That(requester.Labels.TryGetPoint("relief_valve", out var pinned), Is.True);
        Assert.That(Vector3.Distance(pinned, valveCentre), Is.LessThan(0.15f), "the box centre's ray lands on the valve");

        Assert.That(model.MediaType, Is.EqualTo("image/jpeg"));
        Assert.That(model.Image.Take(2), Is.EqualTo(new byte[] { 0xFF, 0xD8 }), "a JPEG goes up, not raw pixels");
        Assert.That(model.Prompt, Does.Contain("- relief_valve: "), "the gazed part is a candidate");
        Assert.That(model.Prompt, Does.Contain("x=0.50, y=0.50"), "an identity-mount camera sees the gaze at the centre");
        Assert.That(requester.LastTiming, Does.StartWith("capture "));
    }

    [UnityTest]
    public IEnumerator When_the_model_fails_the_twin_answers_at_the_gaze_point()
    {
        model.Failure = new ProviderException(ProviderException.UpstreamError, "boom");

        ViewAnswer result = default;
        yield return Await(requester.IdentifyAsync("en", CancellationToken.None), r => result = r);

        Assert.That(result.Identified, Is.True);
        Assert.That(result.Text, Does.Contain("from model data"));
        Assert.That(requester.Labels.TitleFor("relief_valve"), Is.EqualTo(ValveName));
        machine.PartAlong(new Ray(head.position, head.forward), out var gazeHit);
        requester.Labels.TryGetPoint("relief_valve", out var pinned);
        Assert.That(Vector3.Distance(pinned, gazeHit), Is.LessThan(1e-3f));
    }

    [UnityTest]
    public IEnumerator A_slow_model_times_out_and_the_twin_answers()
    {
        model.Hang = true;
        requester.TimeoutSeconds = 0.3f;
        var started = Time.realtimeSinceStartup;

        ViewAnswer result = default;
        yield return Await(requester.IdentifyAsync("en", CancellationToken.None), r => result = r);

        Assert.That(Time.realtimeSinceStartup - started, Is.LessThan(2f));
        Assert.That(result.Text, Does.Contain("from model data"));
    }

    [UnityTest]
    public IEnumerator Without_a_camera_frame_or_a_gazed_part_it_says_why()
    {
        requester.FrameSource.CaptureForTests = (out CameraFrame frame, out string error) =>
        {
            frame = default;
            error = "Camera access was denied.";
            return false;
        };
        head.rotation = Quaternion.LookRotation(Vector3.up); // at the ceiling, no part

        ViewAnswer result = default;
        yield return Await(requester.IdentifyAsync("en", CancellationToken.None), r => result = r);

        Assert.That(result.Identified, Is.False);
        Assert.That(result.Text, Is.EqualTo("Camera access was denied."));
        Assert.That(model.Calls, Is.Zero, "nothing is sent without a frame");
        Assert.That(requester.Labels.ShownCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator Labels_stay_twenty_seconds_and_the_pool_reuses_the_oldest()
    {
        var labels = requester.Labels;
        var a = new FusedAnswer(AnswerSource.Vision, "A", "a", "", 0.9f, null);
        labels.Show(a, Vector3.zero);
        labels.Show(new FusedAnswer(AnswerSource.Vision, "B", "b", "", 0.9f, null), Vector3.one);
        labels.Show(new FusedAnswer(AnswerSource.ModelData, "C", "c", "", 0f, null), Vector3.right);
        labels.Show(new FusedAnswer(AnswerSource.Vision, "A again", "a", "", 0.9f, null), Vector3.up);
        Assert.That(labels.ShownCount, Is.EqualTo(3), "asking about a part again moves its card");
        Assert.That(labels.TitleFor("a"), Is.EqualTo("A again"));

        labels.Show(new FusedAnswer(AnswerSource.Vision, "D", "d", "", 0.9f, null), Vector3.left);
        Assert.That(labels.ShownCount, Is.EqualTo(LabelAnchor.PoolSize));
        Assert.That(labels.TitleFor("b"), Is.Null, "the oldest card went to the newest answer");

        yield return null;
        Assert.That(labels.ShownCount, Is.EqualTo(3), "still showing a frame later");
        labels.HideAll();
        Assert.That(labels.ShownCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator The_assistant_offers_identify_view_only_with_a_vision_model_and_runs_it()
    {
        var loop = UnityEngine.Object.FindAnyObjectByType<VoiceLoop>();
        var chat = new ScriptedChat();
        loop.Configure(chat, null, null);
        chat.Say("Hi.");
        yield return Await(loop.AskAsync("Hello", speak: false), _ => { });
        Assert.That(chat.Requests[0].Tools.Select(t => t.Name), Has.No.Member(FieldmateTools.IdentifyView));
        Assert.That(chat.Requests[0].SystemPrompt, Does.Not.Contain("identify_view"));

        model.Reply = "{\"label\":\"relief valve\",\"part_id\":\"relief_valve\",\"confidence\":0.9,\"bbox\":null,\"one_line_help\":\"\"}";
        loop.Configure(chat, null, null, model);
        chat.Requests.Clear();
        chat.Call(FieldmateTools.IdentifyView, "{}").Say("That's the relief valve.");
        yield return Await(loop.AskAsync("What's this?", speak: false), _ => { });

        Assert.That(chat.Requests[0].Tools.Select(t => t.Name), Has.Member(FieldmateTools.IdentifyView));
        Assert.That(model.Calls, Is.EqualTo(1));
        Assert.That(requester.Labels.TitleFor("relief_valve"), Is.EqualTo(ValveName));
    }

    [UnityTest]
    public IEnumerator Every_capture_shows_the_camera_indicator_for_a_few_seconds()
    {
        var indicator = UnityEngine.Object.FindAnyObjectByType<CaptureIndicator>();
        Assert.That(indicator.IsShown, Is.False);
        model.Reply = "{\"label\":\"relief valve\",\"part_id\":\"relief_valve\",\"confidence\":0.9,\"bbox\":null,\"one_line_help\":\"\"}";

        yield return Await(requester.IdentifyAsync("en", CancellationToken.None), _ => { });

        Assert.That(indicator.IsShown, Is.True, "a frame was taken");
        Assert.That(indicator.Count, Is.EqualTo(1));
        var pill = indicator.GetComponentInChildren<Canvas>(true).transform;
        Assert.That(Vector3.Dot(pill.position - head.position, head.forward), Is.GreaterThan(0.3f), "in front of the user");
        Assert.That(Vector3.Angle(pill.position - head.position, head.forward), Is.LessThan(15f), "inside the view, not at its edge");

        yield return new WaitForSecondsRealtime(CaptureIndicator.Seconds + 0.2f);
        Assert.That(indicator.IsShown, Is.False);
    }

    [UnityTest]
    public IEnumerator Local_only_mode_keeps_the_camera_off_and_the_assistant_can_say_why()
    {
        var button = UnityEngine.Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == "Camera Button");
        var indicator = UnityEngine.Object.FindAnyObjectByType<CaptureIndicator>();
        Assert.That(button.Label, Is.EqualTo("Camera: On"));
        Assert.That(button.transform.IsChildOf(UnityEngine.Object.FindAnyObjectByType<HandMenu>().transform), Is.True, "in the hand menu");

        button.Press();
        yield return null;
        Assert.That(PrivacySettings.LocalOnly, Is.True);
        Assert.That(button.Label, Is.EqualTo("Camera: Off (local-only)"));
        Assert.That(PlayerPrefs.GetInt(PrivacySettings.PrefsKey), Is.EqualTo(1), "kept between sessions");

        ViewAnswer result = default;
        yield return Await(requester.IdentifyAsync("en", CancellationToken.None), r => result = r);
        Assert.That(model.Calls, Is.Zero, "no image leaves the headset");
        Assert.That(indicator.Count, Is.Zero, "and none was taken");
        Assert.That(result.Text, Does.Contain("from model data").And.Contain("Local-only mode is on"), "the twin still answers, and says why");

        var loop = UnityEngine.Object.FindAnyObjectByType<VoiceLoop>();
        var chat = new ScriptedChat();
        loop.Configure(chat, null, null, model);
        chat.Say("Local-only mode is on, so I can't use the camera.");
        yield return Await(loop.AskAsync("Why can't you see what I'm looking at?", speak: false), _ => { });
        Assert.That(chat.Requests[0].Context, Does.Contain("Camera: Local-only mode is on"));

        yield return new WaitForSecondsRealtime(0.7f); // PressButton debounces repeated presses (0.6 s)
        button.Press();
        yield return null;
        Assert.That(PrivacySettings.LocalOnly, Is.False);
        Assert.That(button.Label, Is.EqualTo("Camera: On"));
    }

    private sealed class FakeVision : IVisionModel
    {
        public string Reply = "{}";
        public Exception Failure;
        public bool Hang;
        public int Calls;
        public byte[] Image;
        public string MediaType;
        public string Prompt;

        public async Task<string> AskAsync(byte[] image, string mediaType, string prompt, CancellationToken cancellationToken)
        {
            Calls++;
            Image = image;
            MediaType = mediaType;
            Prompt = prompt;
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Failure != null)
            {
                throw Failure;
            }

            return Reply;
        }
    }

    private sealed class ScriptedChat : IChatModel
    {
        private readonly Queue<ChatResponse> replies = new();
        private int id;
        public List<ChatRequest> Requests { get; } = new();
        public string Name => "scripted";

        public ScriptedChat Call(string tool, string args)
        {
            replies.Enqueue(new ChatResponse("", new[] { new ToolCall($"t{++id}", tool, args) }, StopReason.ToolUse));
            return this;
        }

        public ScriptedChat Say(string text)
        {
            replies.Enqueue(new ChatResponse(text, null, StopReason.EndTurn));
            return this;
        }

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(replies.Dequeue());
        }
    }
}
