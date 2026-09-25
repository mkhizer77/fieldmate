using System.Linq;
using Fieldmate.XR.Spike;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace Fieldmate.Editor;

/// <summary>
/// Builds the platform spike scene (#3) from code so it can be regenerated in batch mode and reviewed as a diff:
/// ARSession, XR Origin with passthrough camera, occlusion, meshing and anchors, a tracked right controller or hand pointer, and
/// <see cref="PlatformSpike"/>. The spike scene is placed first in Build Settings; Main stays enabled after it.
/// </summary>
public static class SpikeSceneBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/Spike/PlatformSpike.unity";
    private const string SpikeDir = "Assets/_Project/Spike";
    private const string MaterialsDir = SpikeDir + "/Materials";
    private const string MeshPrefabPath = SpikeDir + "/SpikeMeshChunk.prefab";

    [MenuItem("Fieldmate/Spike/Build Platform Spike Scene")]
    public static void Build()
    {
        ProjectSetup.EnsureFolder(MaterialsDir);
        ProjectSetup.EnsureFolder(ScenePath[..ScenePath.LastIndexOf('/')]);

        var occluded = CreateMaterial("Occluded", Shader.Find("Fieldmate/Spike/OccludedUnlit"));
        var meshViz = CreateMaterial("MeshViz", Shader.Find("Fieldmate/Spike/MeshViz"));
        var preview = CreateMaterial("CapturePreview", Shader.Find("Universal Render Pipeline/Unlit"));
        var pointer = CreateMaterial("Pointer", Shader.Find("Universal Render Pipeline/Unlit"));
        pointer.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
        var meshPrefab = CreateMeshPrefab(meshViz);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        new GameObject("AR Session", typeof(ARSession));

        var originGo = new GameObject("XR Origin", typeof(XROrigin), typeof(ARAnchorManager));
        var origin = originGo.GetComponent<XROrigin>();
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        var offset = new GameObject("Camera Offset").transform;
        offset.SetParent(originGo.transform, false);
        origin.CameraFloorOffsetObject = offset.gameObject;

        var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(TrackedPoseDriver),
            typeof(ARCameraManager), typeof(AROcclusionManager), typeof(ARShaderOcclusion));
        cameraGo.tag = "MainCamera";
        cameraGo.transform.SetParent(offset, false);
        var camera = cameraGo.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // alpha 0 = passthrough shows through
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50f;
        origin.Camera = camera;
        ConfigurePoseDriver(cameraGo.GetComponent<TrackedPoseDriver>(), new[] { "<XRHMD>/centerEyePosition" }, new[] { "<XRHMD>/centerEyeRotation" });
        cameraGo.GetComponent<AROcclusionManager>().requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;

        var controllerGo = new GameObject("Right Controller", typeof(TrackedPoseDriver));
        controllerGo.transform.SetParent(offset, false);
        // Right controller pointer pose, or the right hand's Meta aim pose when hands are tracked.
        ConfigurePoseDriver(controllerGo.GetComponent<TrackedPoseDriver>(),
            new[] { "<XRController>{RightHand}/pointerPosition", "<MetaAimHand>{RightHand}/devicePosition" },
            new[] { "<XRController>{RightHand}/pointerRotation", "<MetaAimHand>{RightHand}/deviceRotation" });

        // ARMeshManager must be a child of the XR Origin; its scale is the meshing volume.
        var meshingGo = new GameObject("Meshing", typeof(ARMeshManager));
        meshingGo.transform.SetParent(originGo.transform, false);
        meshingGo.transform.localScale = Vector3.one * 10f;
        meshingGo.GetComponent<ARMeshManager>().meshPrefab = meshPrefab;

        var spikeGo = new GameObject("Platform Spike", typeof(PlatformSpike));
        var so = new SerializedObject(spikeGo.GetComponent<PlatformSpike>());
        so.FindProperty("cameraManager").objectReferenceValue = cameraGo.GetComponent<ARCameraManager>();
        so.FindProperty("meshManager").objectReferenceValue = meshingGo.GetComponent<ARMeshManager>();
        so.FindProperty("occlusionManager").objectReferenceValue = cameraGo.GetComponent<AROcclusionManager>();
        so.FindProperty("shaderOcclusion").objectReferenceValue = cameraGo.GetComponent<ARShaderOcclusion>();
        so.FindProperty("anchorManager").objectReferenceValue = originGo.GetComponent<ARAnchorManager>();
        so.FindProperty("head").objectReferenceValue = cameraGo.transform;
        so.FindProperty("rightController").objectReferenceValue = controllerGo.transform;
        so.FindProperty("anchorMaterial").objectReferenceValue = occluded;
        so.FindProperty("previewMaterial").objectReferenceValue = preview;
        so.FindProperty("pointerMaterial").objectReferenceValue = pointer;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        PutFirstInBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SpikeSceneBuilder] Built {ScenePath}");
    }

    private static void ConfigurePoseDriver(TrackedPoseDriver driver, string[] positionBindings, string[] rotationBindings)
    {
        driver.positionInput = new InputActionProperty(CreateAction("Position", "Vector3", positionBindings));
        driver.rotationInput = new InputActionProperty(CreateAction("Rotation", "Quaternion", rotationBindings));
    }

    private static InputAction CreateAction(string name, string controlType, string[] bindings)
    {
        var action = new InputAction(name, expectedControlType: controlType);
        foreach (var binding in bindings)
        {
            action.AddBinding(binding);
        }

        return action;
    }

    private static Material CreateMaterial(string name, Shader shader)
    {
        var path = $"{MaterialsDir}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        return material;
    }

    private static MeshFilter CreateMeshPrefab(Material material)
    {
        var go = new GameObject("SpikeMeshChunk", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, MeshPrefabPath);
        Object.DestroyImmediate(go);
        return prefab.GetComponent<MeshFilter>();
    }

    private static void PutFirstInBuildSettings(string path)
    {
        var others = EditorBuildSettings.scenes.Where(s => s.path != path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) }.Concat(others).ToArray();
    }
}
