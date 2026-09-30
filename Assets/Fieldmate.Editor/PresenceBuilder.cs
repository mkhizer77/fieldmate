using System.Collections.Generic;
using Fieldmate.XR;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Editor;

/// <summary>
/// Hand and controller presence (#55): the runtime's fitted hand mesh (<see cref="TrackedHandMesh"/>, with the XR Hands
/// sample model driven by the tracked skeleton as the fallback) and the XRI generic controller model on the controller
/// pose, all drawn with the light-yellow <c>Fieldmate/PresenceGlow</c> rim instead of the bare passthrough silhouette.
/// Builds the prefabs and places them in the bench scene.
/// </summary>
public static class PresenceBuilder
{
    public const string Dir = "Assets/_Project/Presence";
    public const string MaterialPath = Dir + "/PresenceGlow.mat";
    public const string LeftHandPrefabPath = Dir + "/LeftHandPresence.prefab";
    public const string RightHandPrefabPath = Dir + "/RightHandPresence.prefab";
    public const string ControllerPrefabPath = Dir + "/ControllerPresence.prefab";
    public const string ShaderName = "Fieldmate/PresenceGlow";

    [MenuItem("Fieldmate/Build Presence Prefabs")]
    public static void BuildPrefabs()
    {
        var material = GlowMaterial();
        BuildHand(Handedness.Left, Dir + "/Models/LeftHand.fbx", LeftHandPrefabPath, material);
        BuildHand(Handedness.Right, Dir + "/Models/RightHand.fbx", RightHandPrefabPath, material);
        BuildController(Dir + "/Models/UniversalController.fbx", ControllerPrefabPath, material);
        AssetDatabase.SaveAssets();
        Debug.Log("[PresenceBuilder] Built hand and controller presence prefabs.");
    }

    /// <summary>Adds both hands and both controllers under the camera offset, glowing brighter with their interactor.</summary>
    public static void AddToScene(Transform cameraOffset, XRBaseInteractor leftInteractor, XRBaseInteractor rightInteractor)
    {
        BuildPrefabs();
        Hand(cameraOffset, LeftHandPrefabPath, "Left Hand Presence", leftInteractor);
        Hand(cameraOffset, RightHandPrefabPath, "Right Hand Presence", rightInteractor);
        Controller(cameraOffset, "Left", leftInteractor);
        Controller(cameraOffset, "Right", rightInteractor);
    }

    private static void Hand(Transform parent, string prefabPath, string name, XRBaseInteractor interactor)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<PresenceGlow>().Configure(go.GetComponentInChildren<SkinnedMeshRenderer>(), interactor);
        go.GetComponent<PresenceGlow>().FollowRuntimeMesh(go.GetComponent<TrackedHandMesh>());
    }

    private static void Controller(Transform parent, string side, XRBaseInteractor interactor)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ControllerPrefabPath));
        go.name = $"{side} Controller Presence";
        go.transform.SetParent(parent, false);
        var pose = go.AddComponent<TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(Action("Position", "Vector3",
            $"<OculusTouchController>{{{side}Hand}}/devicePosition", $"<QuestTouchPlusController>{{{side}Hand}}/devicePosition"));
        pose.rotationInput = new InputActionProperty(Action("Rotation", "Quaternion",
            $"<OculusTouchController>{{{side}Hand}}/deviceRotation", $"<QuestTouchPlusController>{{{side}Hand}}/deviceRotation"));
        go.GetComponent<PresenceGlow>().Configure(go.GetComponentInChildren<MeshRenderer>(), interactor);
        if (side == "Left")
        {
            go.GetComponentInChildren<MeshRenderer>().transform.localScale = new Vector3(-1f, 1f, 1f); // mirrored for the left hand
        }
    }

    private static void BuildHand(Handedness handedness, string modelPath, string prefabPath, Material material)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath)
            ?? throw new System.IO.FileNotFoundException(modelPath);
        var root = new GameObject($"{handedness} Hand Presence", typeof(XRHandTrackingEvents), typeof(XRHandSkeletonDriver),
            typeof(XRHandMeshController), typeof(PresenceGlow), typeof(TrackedHandMesh));
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.transform.SetParent(root.transform, false);
        var renderer = visual.GetComponentInChildren<SkinnedMeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.updateWhenOffscreen = true; // bounds follow the tracked joints

        var events = root.GetComponent<XRHandTrackingEvents>();
        events.handedness = handedness;
        events.updateType = XRHandTrackingEvents.UpdateTypes.Dynamic | XRHandTrackingEvents.UpdateTypes.BeforeRender;

        var driver = root.GetComponent<XRHandSkeletonDriver>();
        driver.rootTransform = visual.transform;
        driver.handTrackingEvents = events;
        driver.jointTransformReferences = new List<JointToTransformReference>(); // null on a fresh component
        var missing = new List<string>();
        driver.FindJointsFromRoot(missing);
        if (missing.Count > 0)
        {
            throw new System.InvalidOperationException($"{modelPath} lacks joints: {string.Join(", ", missing)}");
        }

        var mesh = root.GetComponent<XRHandMeshController>();
        mesh.handTrackingEvents = events;
        mesh.handMeshRenderer = renderer;
        mesh.showMeshWhenTrackingIsAcquired = true;
        mesh.hideMeshWhenTrackingIsLost = true;
        renderer.enabled = false; // until tracking is acquired
        root.GetComponent<TrackedHandMesh>().Configure(material, visual); // the runtime's fitted mesh replaces the sample model when available

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
    }

    private static void BuildController(string modelPath, string prefabPath, Material material)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath)
            ?? throw new System.IO.FileNotFoundException(modelPath);
        var root = new GameObject("Controller Presence", typeof(ModalityVisibility), typeof(PresenceGlow));
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.transform.SetParent(root.transform, false);
        foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        root.GetComponent<ModalityVisibility>().Configure(Modality.Controllers, visual);
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
    }

    private static Material GlowMaterial()
    {
        ProjectSetup.EnsureFolder(Dir);
        var shader = Shader.Find(ShaderName) ?? throw new System.InvalidOperationException($"Shader {ShaderName} not found (Assets/_Project/Shaders).");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.shader = shader;
        material.SetColor("_GlowColor", new Color(1f, 0.94f, 0.62f, 1f));
        material.SetFloat("_RimPower", 2.8f); // a thin outline, like the system hand visual
        material.SetFloat("_Fill", 0.03f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static InputAction Action(string name, string controlType, params string[] bindings)
    {
        var action = new InputAction(name, expectedControlType: controlType);
        foreach (var binding in bindings)
        {
            action.AddBinding(binding);
        }

        return action;
    }
}
