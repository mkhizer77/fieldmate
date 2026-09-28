using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Twin;
using Fieldmate.XR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
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
        foreach (var type in new[] { typeof(VoiceLoop), typeof(AssistantPanel), typeof(MachineServices), typeof(PartTag), typeof(MachinePlacement), typeof(PermissionsBootstrap),
                     typeof(RotaryInteractable), typeof(RemovablePart), typeof(ToolItem), typeof(ToolSocket), typeof(MachineControlRouter) })
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

        new GameObject("Permissions", typeof(PermissionsBootstrap));

        var originGo = new GameObject("XR Origin", typeof(XROrigin), typeof(ARAnchorManager));
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

        // Right controller pointer, or the right hand's Meta aim pose when hands are tracked.
        var pointerGo = new GameObject("Right Pointer", typeof(TrackedPoseDriver));
        pointerGo.transform.SetParent(offset, false);
        var pointerPose = pointerGo.GetComponent<TrackedPoseDriver>();
        pointerPose.positionInput = new InputActionProperty(Action("Position", "Vector3",
            "<XRController>{RightHand}/pointerPosition", "<MetaAimHand>{RightHand}/devicePosition"));
        pointerPose.rotationInput = new InputActionProperty(Action("Rotation", "Quaternion",
            "<XRController>{RightHand}/pointerRotation", "<MetaAimHand>{RightHand}/deviceRotation"));

        // Grabbing: one direct interactor per hand. Hands pinch (index finger); controllers use grip. The user sees
        // their real hands in passthrough, so no hand meshes are drawn.
        new GameObject("XR Interaction Manager", typeof(XRInteractionManager));
        var leftHand = HandInteractor(offset, "Left");
        var rightHand = HandInteractor(offset, "Right");

        // Room-scan mesh as invisible colliders, so placement snaps to the real floor (child of the origin, scale = volume).
        var meshingGo = new GameObject("Scene Mesh", typeof(ARMeshManager));
        meshingGo.transform.SetParent(originGo.transform, false);
        meshingGo.transform.localScale = Vector3.one * 10f;
        meshingGo.GetComponent<ARMeshManager>().meshPrefab = SceneMeshColliderPrefab();

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

        var placementGo = new GameObject("Machine Placement", typeof(MachinePlacement));
        var placement = placementGo.GetComponent<MachinePlacement>();
        Set(placement, "machine", skid.transform);
        Set(placement, "head", cameraGo.transform);
        Set(placement, "pointer", pointerGo.transform);
        Set(placement, "anchorManager", originGo.GetComponent<ARAnchorManager>());
        Set(placement, "meshManager", meshingGo.GetComponent<ARMeshManager>());
        Set(placement, "services", services.GetComponent<MachineServices>());
        Set(placement, "pointerMaterial", GuideMaterial());

        var router = new GameObject("Machine Controls", typeof(MachineControlRouter)).GetComponent<MachineControlRouter>();
        router.Configure(services.GetComponent<MachineServices>(), placement, new XRBaseInteractor[] { leftHand, rightHand });

        EditorSceneManager.SaveScene(scene, ScenePath);
        var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[AssistantBenchBuilder] Built {ScenePath}");
    }

    /// <summary>
    /// Placeholder FM-200 skid (about 2.3 m wide including the cabinet) from primitives. Each manual part is a tagged
    /// group; the front (+Z) faces the user after placement, so the gauge, pump cover, valves and breaker are there.
    /// </summary>
    private static GameObject BuildSkidPrefab()
    {
        var steel = Mat("Steel", new Color(0.46f, 0.48f, 0.5f), 0.6f, 0.55f);
        var dark = Mat("DarkSteel", new Color(0.16f, 0.17f, 0.18f), 0.5f, 0.4f);
        var motorBlue = Mat("MotorBlue", new Color(0.1f, 0.28f, 0.58f), 0.2f, 0.6f);
        var pumpRed = Mat("PumpRed", new Color(0.62f, 0.14f, 0.1f), 0.2f, 0.55f);
        var safety = Mat("SafetyYellow", new Color(0.95f, 0.72f, 0.05f), 0f, 0.45f);
        var cabinetGrey = Mat("CabinetGrey", new Color(0.78f, 0.8f, 0.78f), 0.1f, 0.35f);
        var dial = Mat("GaugeWhite", new Color(0.96f, 0.96f, 0.94f), 0f, 0.8f);
        var brass = Mat("Brass", new Color(0.78f, 0.58f, 0.22f), 0.9f, 0.65f);
        var breakerRed = Mat("BreakerRed", new Color(0.8f, 0.08f, 0.06f), 0f, 0.5f);
        var rot90X = Quaternion.Euler(90f, 0f, 0f);
        var rot90Z = Quaternion.Euler(0f, 0f, 90f);

        var root = new GameObject("PlaceholderSkid");

        // Base frame (not a manual part): two rails, cross members and a deck plate.
        var frame = Group(root, "Base frame", null);
        foreach (var z in new[] { -0.3f, 0.3f })
        {
            Shape(frame, PrimitiveType.Cube, new Vector3(0f, 0.05f, z), new Vector3(1.7f, 0.1f, 0.08f), dark);
        }

        foreach (var x in new[] { -0.8f, 0f, 0.8f })
        {
            Shape(frame, PrimitiveType.Cube, new Vector3(x, 0.05f, 0f), new Vector3(0.08f, 0.1f, 0.6f), dark);
        }

        Shape(frame, PrimitiveType.Cube, new Vector3(0f, 0.11f, 0f), new Vector3(1.7f, 0.02f, 0.68f), steel);

        var mount = Group(root, "Motor mount", "motor_mount");
        Shape(mount, PrimitiveType.Cube, new Vector3(-0.45f, 0.14f, 0f), new Vector3(0.52f, 0.04f, 0.42f), steel);
        foreach (var (x, z) in new[] { (-0.66f, -0.16f), (-0.66f, 0.16f), (-0.24f, -0.16f), (-0.24f, 0.16f) })
        {
            Shape(mount, PrimitiveType.Cylinder, new Vector3(x, 0.175f, z), new Vector3(0.035f, 0.015f, 0.035f), dark);
        }

        var motor = Group(root, "Drive motor", "motor");
        Shape(motor, PrimitiveType.Cylinder, new Vector3(-0.45f, 0.37f, 0f), new Vector3(0.34f, 0.24f, 0.34f), motorBlue, rot90Z);
        Shape(motor, PrimitiveType.Cylinder, new Vector3(-0.2f, 0.37f, 0f), new Vector3(0.3f, 0.02f, 0.3f), motorBlue, rot90Z);
        Shape(motor, PrimitiveType.Cube, new Vector3(-0.45f, 0.58f, 0.02f), new Vector3(0.14f, 0.1f, 0.16f), motorBlue);
        Shape(motor, PrimitiveType.Cube, new Vector3(-0.45f, 0.2f, 0f), new Vector3(0.4f, 0.06f, 0.3f), motorBlue);

        var fins = Group(root, "Cooling fins", "cooling_fins");
        Shape(fins, PrimitiveType.Cylinder, new Vector3(-0.72f, 0.37f, 0f), new Vector3(0.32f, 0.04f, 0.32f), dark, rot90Z);
        foreach (var angle in new[] { 20f, 55f, 125f, 160f })
        {
            var r = Quaternion.Euler(angle, 0f, 0f);
            Shape(fins, PrimitiveType.Cube, new Vector3(-0.45f, 0.37f, 0f) + r * new Vector3(0f, 0.175f, 0f), new Vector3(0.44f, 0.02f, 0.02f), motorBlue, r);
        }

        var guard = Group(root, "Coupling guard", null);
        Shape(guard, PrimitiveType.Cube, new Vector3(-0.1f, 0.37f, 0f), new Vector3(0.16f, 0.2f, 0.22f), safety);

        var pump = Group(root, "Pump", "pump");
        Shape(pump, PrimitiveType.Cylinder, new Vector3(0.18f, 0.37f, 0f), new Vector3(0.42f, 0.1f, 0.42f), pumpRed, rot90X);
        Shape(pump, PrimitiveType.Cube, new Vector3(0.18f, 0.18f, 0f), new Vector3(0.3f, 0.1f, 0.24f), pumpRed);
        Shape(pump, PrimitiveType.Cylinder, new Vector3(0.02f, 0.37f, 0f), new Vector3(0.14f, 0.05f, 0.14f), pumpRed, rot90Z);

        // Removable: pull it off towards you; it opens the relief seat behind it.
        var cover = Group(root, "Pump access cover", "pump_cover");
        Shape(cover, PrimitiveType.Cylinder, new Vector3(0.18f, 0.37f, 0.115f), new Vector3(0.3f, 0.015f, 0.3f), pumpRed, rot90X);
        for (var i = 0; i < 6; i++)
        {
            var a = i * Mathf.PI / 3f;
            Shape(cover, PrimitiveType.Cylinder, new Vector3(0.18f + Mathf.Cos(a) * 0.12f, 0.37f + Mathf.Sin(a) * 0.12f, 0.135f),
                new Vector3(0.025f, 0.01f, 0.025f), dark, rot90X);
        }

        cover.AddComponent<RemovablePart>().Configure("pump_cover");
        cover.GetComponent<Rigidbody>().isKinematic = true;
        cover.GetComponent<Rigidbody>().useGravity = false;

        // Behind the cover; the socket takes the new cartridge once the cover is off.
        var seat = Group(root, "Relief valve seat", "relief_valve_seat");
        Shape(seat, PrimitiveType.Cylinder, new Vector3(0.18f, 0.37f, 0.1f), new Vector3(0.08f, 0.01f, 0.08f), steel, rot90X);
        var socketGo = new GameObject("Socket", typeof(SphereCollider), typeof(ToolSocket));
        socketGo.transform.SetParent(seat.transform, false);
        socketGo.transform.localPosition = new Vector3(0.18f, 0.37f, 0.13f);
        socketGo.GetComponent<SphereCollider>().isTrigger = true;
        socketGo.GetComponent<SphereCollider>().radius = 0.07f;
        socketGo.GetComponent<ToolSocket>().Configure("relief_valve_seat");

        var inlet = Group(root, "Inlet valve", "inlet_valve");
        Shape(inlet, PrimitiveType.Cylinder, new Vector3(0.58f, 0.37f, 0f), new Vector3(0.09f, 0.18f, 0.09f), steel, rot90Z);
        Shape(inlet, PrimitiveType.Sphere, new Vector3(0.58f, 0.37f, 0f), new Vector3(0.13f, 0.13f, 0.13f), safety);
        Shape(inlet, PrimitiveType.Cylinder, new Vector3(0.58f, 0.43f, 0f), new Vector3(0.02f, 0.03f, 0.02f), steel);
        Shape(inlet, PrimitiveType.Cylinder, new Vector3(0.36f, 0.37f, 0f), new Vector3(0.09f, 0.08f, 0.09f), steel, rot90Z);

        // Quarter-turn lever: along the pipe is open; pull it a quarter turn towards you to close.
        var lever = Pivot(inlet, "Lever", new Vector3(0.58f, 0.46f, 0f));
        Shape(lever, PrimitiveType.Cube, new Vector3(0.07f, 0f, 0f), new Vector3(0.18f, 0.025f, 0.035f), safety);
        lever.AddComponent<RotaryInteractable>().Configure("inlet_valve", lever.transform, Vector3.down, 0f, 90f, 0f,
            new[] { 0f, 90f }, new[] { "open", "closed" }, 30f, 1);

        var line = Group(root, "Discharge line", "pressure_line");
        Shape(line, PrimitiveType.Cylinder, new Vector3(0.18f, 0.8f, 0f), new Vector3(0.07f, 0.23f, 0.07f), steel);
        Shape(line, PrimitiveType.Cylinder, new Vector3(0.47f, 1.03f, 0f), new Vector3(0.07f, 0.29f, 0.07f), steel, rot90Z);
        Shape(line, PrimitiveType.Sphere, new Vector3(0.18f, 1.03f, 0f), new Vector3(0.08f, 0.08f, 0.08f), steel);

        var gauge = Group(root, "Pressure gauge", "pressure_gauge");
        Shape(gauge, PrimitiveType.Cylinder, new Vector3(0.18f, 0.78f, 0.06f), new Vector3(0.16f, 0.02f, 0.16f), dark, rot90X);
        Shape(gauge, PrimitiveType.Cylinder, new Vector3(0.18f, 0.78f, 0.075f), new Vector3(0.14f, 0.005f, 0.14f), dial, rot90X);
        Shape(gauge, PrimitiveType.Cube, new Vector3(0.2f, 0.795f, 0.08f), new Vector3(0.05f, 0.006f, 0.004f), breakerRed, Quaternion.Euler(0f, 0f, 35f));

        var relief = Group(root, "Relief valve", "relief_valve");
        Shape(relief, PrimitiveType.Cylinder, new Vector3(0.18f, 1.13f, 0f), new Vector3(0.1f, 0.07f, 0.1f), brass);
        Shape(relief, PrimitiveType.Cylinder, new Vector3(0.18f, 1.22f, 0f), new Vector3(0.06f, 0.03f, 0.06f), brass);
        Shape(relief, PrimitiveType.Cylinder, new Vector3(0.18f, 1.13f, 0.08f), new Vector3(0.04f, 0.04f, 0.04f), brass, rot90X);

        var outlet = Group(root, "Outlet valve", "outlet_valve");
        Shape(outlet, PrimitiveType.Cube, new Vector3(0.66f, 1.03f, 0f), new Vector3(0.11f, 0.12f, 0.1f), dark);
        Shape(outlet, PrimitiveType.Cylinder, new Vector3(0.66f, 1.15f, 0f), new Vector3(0.02f, 0.06f, 0.02f), steel);

        // Gate valve handwheel: three turns clockwise (seen from above) to close, a tick every eighth of a turn.
        var wheel = Pivot(outlet, "Handwheel", new Vector3(0.66f, 1.21f, 0f));
        Shape(wheel, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.16f, 0.008f, 0.16f), breakerRed);
        Shape(wheel, PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f), new Vector3(0.15f, 0.01f, 0.012f), dark);
        Shape(wheel, PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f), new Vector3(0.012f, 0.01f, 0.15f), dark);
        Shape(wheel, PrimitiveType.Cylinder, new Vector3(0.065f, 0.03f, 0f), new Vector3(0.02f, 0.025f, 0.02f), dark);
        wheel.AddComponent<RotaryInteractable>().Configure("outlet_valve", wheel.transform, Vector3.up, 0f, 1080f, 0f,
            new[] { 0f, 1080f }, new[] { "open", "closed" }, 45f, 1);

        var cabinet = Group(root, "Electrical cabinet", "electrical_cabinet");
        Shape(cabinet, PrimitiveType.Cube, new Vector3(-1.2f, 0.62f, 0f), new Vector3(0.5f, 0.9f, 0.28f), cabinetGrey);
        Shape(cabinet, PrimitiveType.Cube, new Vector3(-1.2f, 0.62f, 0.142f), new Vector3(0.004f, 0.84f, 0.004f), dark);
        foreach (var x in new[] { -1.4f, -1.0f })
        {
            Shape(cabinet, PrimitiveType.Cube, new Vector3(x, 0.09f, 0f), new Vector3(0.05f, 0.18f, 0.24f), dark);
        }

        var breaker = Group(root, "Main breaker", "main_breaker");
        Shape(breaker, PrimitiveType.Cube, new Vector3(-1.2f, 0.82f, 0.145f), new Vector3(0.16f, 0.16f, 0.01f), safety);
        Shape(breaker, PrimitiveType.Cylinder, new Vector3(-1.2f, 0.82f, 0.16f), new Vector3(0.11f, 0.015f, 0.11f), breakerRed, rot90X);

        // Two-hand rotary isolator: bar horizontal is on; turn it clockwise with both hands to off (90°), then locked (135°).
        var handle = Pivot(breaker, "Handle", new Vector3(-1.2f, 0.82f, 0.18f));
        Shape(handle, PrimitiveType.Cube, Vector3.zero, new Vector3(0.3f, 0.04f, 0.04f), breakerRed);
        handle.AddComponent<RotaryInteractable>().Configure("main_breaker", handle.transform, Vector3.back, 0f, 135f, 0f,
            new[] { 0f, 90f, 135f }, new[] { "on", "off", "locked" }, 45f, 2);

        var tray = Group(root, "Parts tray", null);
        Shape(tray, PrimitiveType.Cube, new Vector3(0.7f, 0.13f, 0.26f), new Vector3(0.2f, 0.02f, 0.12f), dark);

        // The new cartridge: carry it to the seat behind the pump cover.
        var spare = Group(root, "Spare relief cartridge", "relief_cartridge");
        spare.transform.localPosition = new Vector3(0.7f, 0.17f, 0.26f);
        Shape(spare, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.05f, 0.06f, 0.05f), brass, rot90X);
        spare.AddComponent<ToolItem>().Configure("relief_cartridge");
        spare.GetComponent<Rigidbody>().isKinematic = true;
        spare.GetComponent<Rigidbody>().useGravity = false;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, SkidPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// A hand's grab interactor: follows the controller grip, or the hand's pinch point when hands are tracked; selects on
    /// controller grip or an index pinch. A 5 cm trigger sphere finds parts; the kinematic body makes triggers fire.
    /// </summary>
    private static XRDirectInteractor HandInteractor(Transform parent, string side)
    {
        var go = new GameObject($"{side} Hand", typeof(TrackedPoseDriver), typeof(SphereCollider), typeof(Rigidbody), typeof(XRDirectInteractor));
        go.transform.SetParent(parent, false);
        var pose = go.GetComponent<TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(Action("Position", "Vector3",
            $"<OculusTouchController>{{{side}Hand}}/devicePosition", $"<QuestTouchPlusController>{{{side}Hand}}/devicePosition",
            $"<HandInteraction>{{{side}Hand}}/pinchPosition"));
        pose.rotationInput = new InputActionProperty(Action("Rotation", "Quaternion",
            $"<OculusTouchController>{{{side}Hand}}/deviceRotation", $"<QuestTouchPlusController>{{{side}Hand}}/deviceRotation",
            $"<HandInteraction>{{{side}Hand}}/pinchRotation"));

        var sphere = go.GetComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = 0.05f;
        var body = go.GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        var interactor = go.GetComponent<XRDirectInteractor>();
        var select = new InputAction("Select", InputActionType.Button, $"<XRController>{{{side}Hand}}/gripPressed");
        // No fist (graspFirm) binding: a relaxed hand or the left-hand talk pinch reads as a firm grasp and pressed the
        // Start button over and over in the device test (2026-09-28). Index pinch is reliable on Quest 3.
        select.AddBinding($"<MetaAimHand>{{{side}Hand}}/indexPressed");
        interactor.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction;
        interactor.selectInput.inputActionPerformed = select;
        return interactor;
    }

    /// <summary>An untagged child the moving part of a control rotates about.</summary>
    private static GameObject Pivot(GameObject parent, string name, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPosition;
        return go;
    }

    /// <summary>An unscaled group; tagged with a manual part id when <paramref name="partId"/> is set.</summary>
    private static GameObject Group(GameObject parent, string name, string partId)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        if (partId != null)
        {
            Set(go.AddComponent<PartTag>(), "partId", partId);
        }

        return go;
    }

    private static void Shape(GameObject parent, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Material material,
        Quaternion? rotation = null)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = shape.ToString();
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = material;
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

    /// <summary>Collider-only mesh chunk: the room scan is used for placement, not drawn.</summary>
    private static MeshFilter SceneMeshColliderPrefab()
    {
        const string path = "Assets/_Project/Placeholders/SceneMeshCollider.prefab";
        var go = new GameObject("SceneMeshCollider", typeof(MeshFilter), typeof(MeshCollider));
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab.GetComponent<MeshFilter>();
    }

    /// <summary>Vertex-coloured unlit material for the placement line and floor ring.</summary>
    private static Material GuideMaterial()
    {
        var path = $"{MaterialsDir}/PlacementGuide.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", new Color(0.2f, 0.9f, 1f, 1f));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Mat(string name, Color color, float metallic = 0f, float smoothness = 0.5f)
    {
        var path = $"{MaterialsDir}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.enableInstancing = true;
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
