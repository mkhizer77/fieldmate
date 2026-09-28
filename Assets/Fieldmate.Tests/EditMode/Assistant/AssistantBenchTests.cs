using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Editor;
using Fieldmate.Twin;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fieldmate.Tests.EditMode.Assistant;

public class AssistantBenchTests
{
    private Scene scene;

    [OneTimeSetUp]
    public void Open() => scene = EditorSceneManager.OpenScene(AssistantBenchBuilder.ScenePath, OpenSceneMode.Additive);

    [OneTimeTearDown]
    public void Close() => EditorSceneManager.CloseScene(scene, true);

    private T[] All<T>() where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

    [Test]
    public void PlaceholderSkid_TagsEveryManualPart()
    {
        var tags = All<PartTag>().Select(t => t.PartId);
        var report = ManualValidator.Validate(System.IO.File.ReadAllText(ManualValidator.ManualPath), tags);

        Assert.That(report.ManualProblems, Is.Empty);
        Assert.That(report.MissingInScene, Is.Empty, "every manual part needs a placeholder");
        Assert.That(report.UnknownTags, Is.Empty);
    }

    [Test]
    public void TaggedParts_HaveCollidersForGaze()
    {
        Assert.That(All<PartTag>().All(t => t.GetComponent<Collider>() != null), Is.True);
    }

    [Test]
    public void VoiceLoop_IsFullyWired()
    {
        var loop = All<VoiceLoop>().Single();
        var so = new SerializedObject(loop);
        foreach (var name in new[] { "machine", "panel", "highlighter", "player", "head" })
        {
            Assert.That(so.FindProperty(name).objectReferenceValue, Is.Not.Null, name);
        }

        Assert.That(new SerializedObject(All<MachineServices>().Single()).FindProperty("manualJson").objectReferenceValue, Is.Not.Null);
    }

    [Test]
    public void Bench_IsTheFirstBuildScene()
    {
        Assert.That(EditorBuildSettings.scenes.First().path, Is.EqualTo(AssistantBenchBuilder.ScenePath));
    }
}

public class AudioRingBufferTests
{
    [Test]
    public void WritesAndReadsInOrder_PaddingWithSilence()
    {
        var ring = new AudioRingBuffer(4);
        Assert.That(ring.Write(new[] { 1f, 2f, 3f }, 3), Is.EqualTo(3));

        var output = new float[5];
        Assert.That(ring.Read(output), Is.EqualTo(3));
        Assert.That(output, Is.EqualTo(new[] { 1f, 2f, 3f, 0f, 0f }));
        Assert.That(ring.Count, Is.Zero);
    }

    [Test]
    public void WrapsAround_AndDropsWhatDoesNotFit()
    {
        var ring = new AudioRingBuffer(4);
        ring.Write(new[] { 1f, 2f, 3f }, 3);
        ring.Read(new float[2]);

        Assert.That(ring.Write(new[] { 4f, 5f, 6f, 7f }, 4), Is.EqualTo(3), "only 3 free slots");
        var output = new float[4];
        ring.Read(output);
        Assert.That(output, Is.EqualTo(new[] { 3f, 4f, 5f, 6f }));
    }

    [Test]
    public void Clear_EmptiesTheBuffer()
    {
        var ring = new AudioRingBuffer(8);
        ring.Write(new[] { 1f, 2f }, 2);
        ring.Clear();

        Assert.That(ring.Count, Is.Zero);
        Assert.That(ring.Capacity, Is.EqualTo(8));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new AudioRingBuffer(0));
    }
}
