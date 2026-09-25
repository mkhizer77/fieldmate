using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Twin;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace Fieldmate.Editor;

/// <summary>
/// Builds the voice-assistant test bench (#15): passthrough XR rig, a placeholder pump skid made of primitives with a
/// <see cref="PartTag"/> for every manual part, <see cref="MachineServices"/>, and the <see cref="VoiceLoop"/> with its
/// panel. The skid is a prefab in _Project/Placeholders so the real machine placement (#5) can reuse it.
/// </summary>
public static class AssistantBenchBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/AssistantBench.unity";
    public const string SkidPrefabPath = "Assets/_Project/Placeholders/PlaceholderSkid.prefab";
    private const string MaterialsDir = "Assets/_Project/Placeholders/Materials";
    private const string ManualPath = "Assets/_Project/Manual/manual.json";

    [MenuItem("Fieldmate/Build Assistant Bench Scene")]
    public static void Build()
    {
        // Scripts created in this session must be imported assets, or the saved scene references in-memory scripts
        // that don't survive a reload (seen when the builder ran right after the scripts were first compiled).
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var type in new[] { typeof(VoiceLoop), typeof(AssistantPanel), typeof(MachineServices), typeof(PartTag) })
        {
            if (!AssetDatabase.FindAssets($"t:MonoScript {type.Name}").Any())
            {
                throw new System.InvalidOperationException($"Script {type.Name} is not an imported asset yet; run the builder again.");
            }
        }

        ProjectSetup.EnsureFolder(MaterialsDir);
        ProjectSetup.EnsureFolder(ScenePath[..ScenePath.LastIndexOf('/')]);
        var skidPrefab = BuildSkidPrefab();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("AR Session", typeof(ARSession));
        var light = new GameObject("Directional Light", typeof(Light));
        light.GetComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var originGo = new GameObject("XR Origin", typeof(XROrigin));
        var origin = originGo.GetComponent<XROrigin>();
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        var offset = new GameObject("Camera Offset").transform;
        offset.SetParent(originGo.transform, false);
        origin.CameraFloorOffsetObject = offset.gameObject;

        var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(TrackedPoseDriver), typeof(ARCameraManager));
        cameraGo.tag = "MainCamera";
        cameraGo.transform.SetParent(offset, false);
        cameraGo.transform.localPosition = new Vector3(0f, 1.6f, 0f); // editor preview height; tracking overrides on device
        var camera = cameraGo.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.nearClipPlane = 0.05f;
        origin.Camera = camera;
        var pose = cameraGo.GetComponent<TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        pose.rotationInput = new InputActionProperty(new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));

        var skid = (GameObject)PrefabUtility.InstantiatePrefab(skidPrefab);
        skid.transform.SetPositionAndRotation(new Vector3(0f, 0f, 1.8f), Quaternion.Euler(0f, 180f, 0f));

        var services = new GameObject("Machine Services", typeof(MachineServices));
        Set(services.GetComponent<MachineServices>(), "manualJson", AssetDatabase.LoadAssetAtPath<TextAsset>(ManualPath));

        var assistant = new GameObject("Assistant", typeof(VoiceLoop), typeof(PartHighlighter));
        var panel = new GameObject("Assistant Panel", typeof(RectTransform), typeof(AssistantPanel));
        panel.transform.SetParent(assistant.transform, false);
        panel.transform.position = new Vector3(0.5f, 1.45f, 1.1f);
        var audio = new GameObject("Assistant Voice", typeof(AudioSource), typeof(StreamingAudioPlayer));
        audio.transform.SetParent(assistant.transform, false);

        var loop = assistant.GetComponent<VoiceLoop>();
        Set(loop, "machine", services.GetComponent<MachineServices>());
        Set(loop, "panel", panel.GetComponent<AssistantPanel>());
        Set(loop, "highlighter", assistant.GetComponent<PartHighlighter>());
        Set(loop, "player", audio.GetComponent<StreamingAudioPlayer>());
        Set(loop, "head", cameraGo.transform);

        EditorSceneManager.SaveScene(scene, ScenePath);
        var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[AssistantBenchBuilder] Built {ScenePath}");
    }

    /// <summary>Placeholder FM-200 skid (~1.6 m long) from primitives; every manual part gets a tagged object.</summary>
    private static GameObject BuildSkidPrefab()
    {
        var steel = Mat("Steel", new Color(0.45f, 0.47f, 0.5f));
        var motorBlue = Mat("MotorBlue", new Color(0.12f, 0.3f, 0.62f));
        var pumpRed = Mat("PumpRed", new Color(0.7f, 0.18f, 0.12f));
        var valveYellow = Mat("ValveYellow", new Color(0.95f, 0.75f, 0.1f));
        var cabinetGrey = Mat("CabinetGrey", new Color(0.72f, 0.74f, 0.7f));
        var dial = Mat("GaugeWhite", new Color(0.95f, 0.95f, 0.95f));
        var brass = Mat("Brass", new Color(0.8f, 0.6f, 0.25f));

        var root = new GameObject("PlaceholderSkid");
        Box(root, "Base frame", new Vector3(0f, 0.05f, 0f), new Vector3(1.6f, 0.1f, 0.7f), steel);

        Part(root, "motor", PrimitiveType.Cylinder, new Vector3(-0.45f, 0.33f, 0f), new Vector3(0.36f, 0.25f, 0.36f), motorBlue, Quaternion.Euler(0, 0, 90));
        Part(root, "cooling_fins", PrimitiveType.Cube, new Vector3(-0.72f, 0.33f, 0f), new Vector3(0.04f, 0.38f, 0.38f), steel);
        Part(root, "motor_mount", PrimitiveType.Cube, new Vector3(-0.45f, 0.13f, 0f), new Vector3(0.42f, 0.06f, 0.4f), steel);

        Part(root, "pump", PrimitiveType.Cylinder, new Vector3(0.15f, 0.33f, 0f), new Vector3(0.4f, 0.15f, 0.4f), pumpRed, Quaternion.Euler(0, 0, 90));
        Part(root, "pump_cover", PrimitiveType.Cube, new Vector3(0.15f, 0.33f, -0.22f), new Vector3(0.28f, 0.28f, 0.04f), pumpRed);
        Part(root, "relief_valve_seat", PrimitiveType.Cylinder, new Vector3(0.15f, 0.33f, -0.19f), new Vector3(0.07f, 0.01f, 0.07f), steel, Quaternion.Euler(90, 0, 0));
        Part(root, "relief_cartridge", PrimitiveType.Cylinder, new Vector3(0.62f, 0.13f, -0.25f), new Vector3(0.05f, 0.06f, 0.05f), brass);

        Part(root, "inlet_valve", PrimitiveType.Cylinder, new Vector3(0.15f, 0.33f, 0.32f), new Vector3(0.1f, 0.08f, 0.1f), valveYellow, Quaternion.Euler(90, 0, 0));
        Part(root, "pressure_line", PrimitiveType.Cylinder, new Vector3(0.45f, 0.62f, 0f), new Vector3(0.07f, 0.3f, 0.07f), steel);
        Part(root, "relief_valve", PrimitiveType.Cylinder, new Vector3(0.45f, 0.95f, 0f), new Vector3(0.1f, 0.06f, 0.1f), brass);
        Part(root, "pressure_gauge", PrimitiveType.Cylinder, new Vector3(0.45f, 0.8f, -0.08f), new Vector3(0.14f, 0.015f, 0.14f), dial, Quaternion.Euler(90, 0, 0));
        Part(root, "outlet_valve", PrimitiveType.Cylinder, new Vector3(0.72f, 0.62f, 0f), new Vector3(0.1f, 0.08f, 0.1f), valveYellow, Quaternion.Euler(0, 0, 90));

        Part(root, "electrical_cabinet", PrimitiveType.Cube, new Vector3(-0.95f, 0.75f, 0.2f), new Vector3(0.45f, 1.1f, 0.3f), cabinetGrey);
        Part(root, "main_breaker", PrimitiveType.Cylinder, new Vector3(-0.95f, 0.85f, 0.04f), new Vector3(0.13f, 0.02f, 0.13f), Mat("BreakerRed", new Color(0.8f, 0.1f, 0.1f)), Quaternion.Euler(90, 0, 0));

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, SkidPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject Part(GameObject parent, string partId, PrimitiveType shape, Vector3 localPosition, Vector3 localScale,
        Material material, Quaternion? rotation = null)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = partId;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        var tag = go.AddComponent<PartTag>();
        Set(tag, "partId", partId);
        return go;
    }

    private static void Box(GameObject parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material Mat(string name, Color color)
    {
        var path = $"{MaterialsDir}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Set(Object target, string property, object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(property);
        if (value is string s)
        {
            p.stringValue = s;
        }
        else
        {
            p.objectReferenceValue = (Object)value;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
