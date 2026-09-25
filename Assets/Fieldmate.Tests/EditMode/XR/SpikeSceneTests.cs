using System.Linq;
using Fieldmate.XR.Spike;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>Checks the generated platform spike scene against the Meta OpenXR setup requirements.</summary>
public class SpikeSceneTests
{
    private const string ScenePath = "Assets/_Project/Scenes/Spike/PlatformSpike.unity";
    private Scene scene;

    [OneTimeSetUp]
    public void OpenScene() => scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    [OneTimeTearDown]
    public void CloseScene() => EditorSceneManager.CloseScene(scene, true);

    private T Find<T>() where T : Component =>
        scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<T>(true)).FirstOrDefault();

    [Test]
    public void HasSessionAndFloorOrigin()
    {
        Assert.That(Find<ARSession>(), Is.Not.Null);
        var origin = Find<XROrigin>();
        Assert.That(origin, Is.Not.Null);
        Assert.That(origin.RequestedTrackingOriginMode, Is.EqualTo(XROrigin.TrackingOriginMode.Floor));
        Assert.That(origin.Camera, Is.Not.Null);
    }

    [Test]
    public void Camera_IsTransparentForPassthrough()
    {
        var camera = Find<XROrigin>().Camera;

        Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
        Assert.That(camera.backgroundColor.a, Is.EqualTo(0f));
        Assert.That(camera.GetComponent<ARCameraManager>(), Is.Not.Null);
    }

    [Test]
    public void Camera_HasDepthOcclusion()
    {
        var camera = Find<XROrigin>().Camera;
        var occlusion = camera.GetComponent<AROcclusionManager>();

        Assert.That(occlusion, Is.Not.Null);
        Assert.That(occlusion.requestedEnvironmentDepthMode, Is.Not.EqualTo(EnvironmentDepthMode.Disabled));
        Assert.That(camera.GetComponent<ARShaderOcclusion>(), Is.Not.Null);
    }

    [Test]
    public void MeshManager_IsChildOfOriginWithPrefab()
    {
        var meshManager = Find<ARMeshManager>();

        Assert.That(meshManager, Is.Not.Null);
        Assert.That(meshManager.GetComponent<XROrigin>(), Is.Null, "ARMeshManager must be on a child of the XR Origin.");
        Assert.That(meshManager.GetComponentInParent<XROrigin>(), Is.Not.Null);
        Assert.That(meshManager.meshPrefab, Is.Not.Null);
        Assert.That(meshManager.transform.localScale.x, Is.GreaterThan(1f), "Scale is the meshing volume.");
    }

    [Test]
    public void AnchorManager_IsOnOrigin()
    {
        Assert.That(Find<XROrigin>().GetComponent<ARAnchorManager>(), Is.Not.Null);
    }

    [Test]
    public void Spike_HasAllReferencesAssigned()
    {
        var spike = Find<PlatformSpike>();
        Assert.That(spike, Is.Not.Null);

        var so = new SerializedObject(spike);
        var iterator = so.GetIterator();
        iterator.NextVisible(true); // m_Script
        while (iterator.NextVisible(false))
        {
            if (iterator.propertyType == SerializedPropertyType.ObjectReference)
            {
                Assert.That(iterator.objectReferenceValue, Is.Not.Null, $"{iterator.name} is not assigned.");
            }
        }
    }

    [Test]
    public void AnchorMaterial_UsesOcclusionShader()
    {
        var material = (Material)new SerializedObject(Find<PlatformSpike>()).FindProperty("anchorMaterial").objectReferenceValue;

        Assert.That(material.shader.name, Is.EqualTo("Fieldmate/Spike/OccludedUnlit"));
        Assert.That(material.shader.isSupported, Is.True);
    }
}
