using System.Collections;
using Fieldmate.XR;
using Unity.Collections;
using UnityEngine.XR.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.UI;

/// <summary>#55: the glow settles at its idle level and controller visuals follow the input modality.</summary>
public class PresencePlayModeTests
{
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    [TearDown]
    public void ResetModality() => InputModalityProbe.Set(Modality.Hands);

    [UnityTest]
    public IEnumerator Glow_writes_its_intensity_to_the_renderer()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var glow = go.AddComponent<PresenceGlow>();
        glow.Configure(go.GetComponent<Renderer>(), null);
        yield return null;
        var block = new MaterialPropertyBlock();
        go.GetComponent<Renderer>().GetPropertyBlock(block);
        Assert.That(block.GetFloat(IntensityId), Is.EqualTo(glow.Intensity).Within(1e-4f));
        Assert.That(glow.Intensity, Is.GreaterThan(0f), "a quiet halo at rest");
        Object.Destroy(go);
    }

    [UnityTest]
    public IEnumerator Runtime_hand_mesh_builds_a_skinned_hand_and_hides_the_fallback()
    {
        var root = new GameObject("Hand", typeof(XRHandTrackingEvents));
        var fallback = new GameObject("Fallback");
        fallback.transform.SetParent(root.transform);
        var hand = root.AddComponent<TrackedHandMesh>();
        hand.Configure(new Material(Shader.Find("Fieldmate/PresenceGlow")), fallback);
        yield return null;

        // One triangle weighted to the wrist and index tip, like a slice of the runtime's data.
        var positions = new NativeArray<Vector3>(new[] { Vector3.zero, Vector3.right * 0.1f, Vector3.up * 0.1f }, Allocator.Temp);
        var normals = new NativeArray<Vector3>(new[] { Vector3.forward, Vector3.forward, Vector3.forward }, Allocator.Temp);
        var uvs = new NativeArray<Vector2>(0, Allocator.Temp);
        var indices = new NativeArray<int>(new[] { 0, 1, 2 }, Allocator.Temp);
        var bonesPerVertex = new NativeArray<byte>(new byte[] { 1, 1, 1 }, Allocator.Temp);
        var weights = new NativeArray<BoneWeight1>(new[]
        {
            new BoneWeight1 { boneIndex = XRHandJointID.Wrist.ToIndex(), weight = 1f },
            new BoneWeight1 { boneIndex = XRHandJointID.IndexTip.ToIndex(), weight = 1f },
            new BoneWeight1 { boneIndex = XRHandJointID.Wrist.ToIndex(), weight = 1f },
        }, Allocator.Temp);
        var bind = new NativeArray<Matrix4x4>(XRHandJointID.EndMarker.ToIndex(), Allocator.Temp);
        for (var i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.identity;
        hand.Build(positions, normals, uvs, indices, bonesPerVertex, weights, bind);

        Assert.That(hand.IsBuilt);
        Assert.That(hand.BoneCount, Is.EqualTo(26));
        Assert.That(hand.Renderer.sharedMesh.vertexCount, Is.EqualTo(3));
        Assert.That(hand.Renderer.bones.Length, Is.EqualTo(26));
        Assert.That(hand.Renderer.sharedMesh.bindposes.Length, Is.EqualTo(26));
        Assert.That(hand.Renderer.sharedMaterial.shader.name, Is.EqualTo("Fieldmate/PresenceGlow"));
        Assert.That(fallback.activeSelf, Is.False, "the sample model gives way to the runtime mesh");
        Assert.That(hand.Renderer.enabled, Is.False, "not tracked in the editor");
        Object.Destroy(root);
    }

    [UnityTest]
    public IEnumerator Controller_visual_follows_the_modality()
    {
        InputModalityProbe.Set(Modality.Hands);
        var root = new GameObject("Controller");
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform);
        var visibility = root.AddComponent<ModalityVisibility>();
        visibility.Configure(Modality.Controllers, visual);
        visibility.enabled = false;
        visibility.enabled = true; // re-run OnEnable with the configuration
        yield return null;
        Assert.That(visual.activeSelf, Is.False, "hands: no controller model");
        InputModalityProbe.Set(Modality.Controllers);
        Assert.That(visual.activeSelf, Is.True);
        InputModalityProbe.Set(Modality.Hands);
        Assert.That(visual.activeSelf, Is.False);
        Object.Destroy(root);
    }
}
