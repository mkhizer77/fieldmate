using Fieldmate.AI;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// The assistant as a hologram (#69): a half-body figure projected above a pedestal beside the machine, in place of
    /// the chat panel. It breathes, blinks and looks at the user; it brightens while listening, its scanlines race while
    /// it thinks, and its lips move with the speech that is actually playing (<see cref="LipSync"/> on the player's
    /// output). Built in code from lathe meshes (no downloaded or paid assets); a modelled figure can replace
    /// <see cref="Build"/> later behind the same component. No per-frame allocations.
    /// </summary>
    public sealed class HologramMate : MonoBehaviour
    {
        /// <summary>World height of the projector top (the figure's waist sits just above it).</summary>
        public const float PedestalHeight = 0.92f;

        /// <summary>The figure is a little under life size so it never looms over the user.</summary>
        public const float FigureScale = 0.88f;

        private const float HeadHeight = 0.745f; // eye line, figure space
        private const float MaxHeadYaw = 40f;
        private const float MaxHeadPitch = 20f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int ScanSpeedId = Shader.PropertyToID("_ScanSpeed");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        private static readonly int FadeRangeId = Shader.PropertyToID("_FadeRange");
        private static readonly int FillId = Shader.PropertyToID("_Fill");

        private static readonly Color Cyan = new(0.25f, 0.85f, 1f, 1f);
        private static readonly Color ListenTint = new(0.35f, 1f, 0.8f, 1f);
        private static readonly Color ThinkTint = new(0.45f, 0.7f, 1f, 1f);

        [SerializeField] private Transform head;
        [SerializeField] private Transform anchor;
        [SerializeField] private Vector3 anchorOffset = new(1.2f, 0f, 0.35f);
        [SerializeField] private StreamingAudioPlayer player;

        private readonly LipSync lips = new();
        private ISpeechOutput speechOverride;
        private Transform figure;
        private Transform body;
        private Transform headPivot;
        private Transform upperLip;
        private Transform lowerLip;
        private Transform mouthGlow;
        private Transform leftEye;
        private Transform rightEye;
        private Transform ring;
        private Material skin;
        private Material features;
        private AssistantState state;
        private float intensity = 1f;
        private float scanSpeed = 0.12f;
        private Color tint = Cyan;
        private float nextBlink = 3f;
        private float blinkUntil;
        private float yaw;

        public AssistantState State => state;

        /// <summary>Mouth openness 0..1 this frame.</summary>
        public float MouthOpen => lips.Open;

        /// <summary>Distance the lower lip has dropped from closed, in figure space (tests).</summary>
        public float LipGap => upperLip != null ? upperLip.localPosition.y - lowerLip.localPosition.y : 0f;

        public Transform Figure => figure;
        public Transform HeadPivot => headPivot;
        public bool IsBlinking => Time.time < blinkUntil;

        public void Configure(Transform headTransform, Transform machine, Vector3 localOffset, StreamingAudioPlayer speech)
        {
            head = headTransform;
            anchor = machine;
            anchorOffset = localOffset;
            player = speech;
        }

        public void SetHead(Transform headTransform) => head = headTransform;

        /// <summary>Another speech source than the scene's player (tests).</summary>
        public void UseSpeech(ISpeechOutput output) => speechOverride = output;

        private ISpeechOutput Speech => speechOverride ?? player;

        public void Anchor(Transform machine, Vector3 localOffset)
        {
            anchor = machine;
            anchorOffset = localOffset;
        }

        public void SetState(AssistantState next)
        {
            state = next;
            if (next != AssistantState.Speaking && (Speech == null || !Speech.IsPlaying))
            {
                lips.Close();
            }
        }

        private void Awake() => Build();

        private void Update()
        {
            var dt = Time.deltaTime;
            var now = Time.time;

            // Mouth: the loudness of what the user hears right now; shut the moment speech stops.
            var speech = Speech;
            var speaking = speech != null && speech.IsPlaying;
            if (speaking)
            {
                lips.Update(speech.OutputRms(), dt);
            }
            else if (lips.Open > 0f)
            {
                lips.Update(0f, dt);
            }

            var open = lips.Open;
            upperLip.localPosition = new Vector3(0f, MouthY + 0.004f * open, MouthZ);
            lowerLip.localPosition = new Vector3(0f, MouthY - 0.003f - 0.02f * open, MouthZ);
            mouthGlow.localScale = new Vector3(0.036f, Mathf.Max(0.0005f, 0.022f * open), 1f);
            mouthGlow.localPosition = new Vector3(0f, MouthY - 0.001f - 0.009f * open, MouthZ - 0.002f);

            // Blink every few seconds.
            if (now >= nextBlink)
            {
                blinkUntil = now + 0.12f;
                nextBlink = now + Random.Range(2.5f, 5.5f);
            }

            var eyeY = now < blinkUntil ? 0.15f : 1f;
            leftEye.localScale = new Vector3(0.022f, 0.009f * eyeY, 1f);
            rightEye.localScale = leftEye.localScale;

            // Breathing and a slow hover.
            body.localScale = new Vector3(1f, 1f + 0.012f * Mathf.Sin(now * 1.6f), 1f + 0.018f * Mathf.Sin(now * 1.6f));
            figure.localPosition = new Vector3(0f, PedestalHeight + 0.02f + 0.006f * Mathf.Sin(now * 0.9f), 0f);
            skin.SetFloat(FadeStartId, figure.position.y - 0.02f); // the waist dissolves into the beam

            // Look state on the light itself.
            var (targetIntensity, targetScan, targetTint) = state switch
            {
                AssistantState.Listening => (1.45f, 0.12f, ListenTint),
                AssistantState.Transcribing or AssistantState.Thinking => (1.15f, 0.55f, ThinkTint),
                AssistantState.Speaking => (1.3f, 0.18f, Cyan),
                _ => (1f, 0.12f, Cyan),
            };
            if (speaking)
            {
                (targetIntensity, targetScan, targetTint) = (1.3f, 0.18f, Cyan);
            }

            var k = 1f - Mathf.Exp(-dt / 0.2f);
            intensity = Mathf.Lerp(intensity, targetIntensity + 0.25f * open, k);
            scanSpeed = Mathf.Lerp(scanSpeed, targetScan, k);
            tint = Color.Lerp(tint, targetTint, k);
            skin.SetFloat(IntensityId, intensity);
            skin.SetFloat(ScanSpeedId, scanSpeed);
            skin.SetColor(BaseColorId, tint);
            features.SetFloat(IntensityId, 1.6f + 0.6f * open);
            features.SetColor(BaseColorId, tint);

            ring.localRotation = Quaternion.Euler(0f, now * (state is AssistantState.Thinking or AssistantState.Transcribing ? 160f : 25f), 0f);
        }

        private void LateUpdate()
        {
            if (anchor != null)
            {
                var p = anchor.TransformPoint(anchorOffset);
                transform.position = new Vector3(p.x, anchor.position.y, p.z); // stands on the machine's floor
            }

            if (head == null)
            {
                return;
            }

            // The body turns to the user slowly; the head follows the eyes within a comfortable range.
            var toHead = head.position - transform.position;
            toHead.y = 0f;
            if (toHead.sqrMagnitude > 1e-4f)
            {
                var target = Mathf.Atan2(toHead.x, toHead.z) * Mathf.Rad2Deg;
                yaw = Mathf.LerpAngle(yaw, target, 1f - Mathf.Exp(-Time.deltaTime / 0.8f));
                figure.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            var local = figure.InverseTransformPoint(head.position) - new Vector3(0f, HeadHeight, 0f);
            var headYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -MaxHeadYaw, MaxHeadYaw);
            var headPitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -MaxHeadPitch, MaxHeadPitch);
            var tilt = state == AssistantState.Listening ? 6f : 0f; // a small head tilt while it listens
            var look = Quaternion.Euler(headPitch, headYaw, tilt);
            headPivot.localRotation = Quaternion.Slerp(headPivot.localRotation, look, 1f - Mathf.Exp(-Time.deltaTime / 0.25f));
        }

        private const float MouthY = -0.055f; // relative to the head pivot (the eye line)
        private const float MouthZ = 0.092f; // just in front of the skull surface (0.087 at the mouth line)

        private void Build()
        {
            var shader = Shader.Find("Fieldmate/Hologram");
            skin = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Hologram" };
            skin.SetFloat(FadeRangeId, 0.2f);
            features = new Material(skin) { name = "Hologram Features" };
            features.SetFloat(FillId, 0.9f);
            features.SetFloat(FadeStartId, -10000f); // eyes, lips, lens and ring never fade

            // Projector: a dark pedestal (solid, occluded like the machine) with a glowing lens and a slow ring.
            var solid = Shader.Find("Fieldmate/OccludedLit");
            var steel = new Material(solid != null ? solid : Shader.Find("Universal Render Pipeline/Lit")) { name = "Pedestal" };
            steel.SetColor(BaseColorId, new Color(0.13f, 0.14f, 0.16f));
            Part("Pedestal", transform, LatheMesh.Build("Pedestal", new[]
            {
                new Vector2(0.17f, 0f), new Vector2(0.17f, 0.03f), new Vector2(0.08f, 0.06f), new Vector2(0.06f, 0.2f),
                new Vector2(0.06f, PedestalHeight - 0.08f), new Vector2(0.12f, PedestalHeight - 0.03f),
                new Vector2(0.13f, PedestalHeight), new Vector2(0f, PedestalHeight),
            }, 32), steel, Vector3.zero);
            var lens = new Material(features) { name = "Projector Lens" };
            Part("Lens", transform, LatheMesh.Build("Lens", new[]
            {
                new Vector2(0.1f, 0f), new Vector2(0.1f, 0.006f), new Vector2(0f, 0.008f),
            }, 32), lens, new Vector3(0f, PedestalHeight, 0f));
            ring = Part("Ring", transform, LatheMesh.Build("Ring", new[]
            {
                new Vector2(0.15f, 0f), new Vector2(0.155f, 0.004f), new Vector2(0.15f, 0.008f), new Vector2(0.145f, 0.004f), new Vector2(0.15f, 0f),
            }, 48), features, new Vector3(0f, PedestalHeight + 0.01f, 0f)).transform;

            figure = new GameObject("Figure").transform;
            figure.SetParent(transform, false);
            figure.localPosition = new Vector3(0f, PedestalHeight + 0.02f, 0f);
            figure.localScale = Vector3.one * FigureScale;

            // Torso from the waist (faded into the beam) to the neck; elliptical, shoulders wider than deep.
            body = new GameObject("Body").transform;
            body.SetParent(figure, false);
            Part("Torso", body, LatheMesh.Build("Torso", new[]
            {
                new Vector2(0.13f, 0f), new Vector2(0.14f, 0.08f), new Vector2(0.155f, 0.18f), new Vector2(0.175f, 0.28f),
                new Vector2(0.19f, 0.36f), new Vector2(0.19f, 0.42f), new Vector2(0.165f, 0.47f), new Vector2(0.11f, 0.505f),
                new Vector2(0.055f, 0.52f), new Vector2(0.048f, 0.56f), new Vector2(0.046f, 0.6f), new Vector2(0f, 0.6f),
            }, 40, depth: 0.58f), skin, Vector3.zero);
            foreach (var side in new[] { -1f, 1f })
            {
                // Upper arms hang from the shoulders, slightly away from the body; the beam fades them at the elbow.
                var arm = Part(side < 0f ? "Left Arm" : "Right Arm", body, LatheMesh.Build("Arm", new[]
                {
                    new Vector2(0f, -0.3f), new Vector2(0.03f, -0.29f), new Vector2(0.04f, -0.24f), new Vector2(0.046f, -0.1f),
                    new Vector2(0.05f, -0.02f), new Vector2(0.04f, 0.02f), new Vector2(0f, 0.035f),
                }, 20), skin, new Vector3(side * 0.2f, 0.44f, 0f));
                arm.transform.localRotation = Quaternion.Euler(0f, 0f, side * 7f);
            }

            headPivot = new GameObject("Head").transform;
            headPivot.SetParent(figure, false);
            headPivot.localPosition = new Vector3(0f, HeadHeight, 0.01f);
            Part("Skull", headPivot, LatheMesh.Build("Head", new[]
            {
                new Vector2(0f, -0.125f), new Vector2(0.03f, -0.122f), new Vector2(0.055f, -0.108f), new Vector2(0.07f, -0.085f),
                new Vector2(0.079f, -0.05f), new Vector2(0.085f, -0.01f), new Vector2(0.088f, 0.03f), new Vector2(0.085f, 0.07f),
                new Vector2(0.073f, 0.1f), new Vector2(0.05f, 0.122f), new Vector2(0.02f, 0.132f), new Vector2(0f, 0.134f),
            }, 36, depth: 1.12f), skin, Vector3.zero);

            // Face: glowing eyes and lips, drawn a hair in front of the skull so they read from any angle.
            var quad = QuadMesh();
            leftEye = Part("Left Eye", headPivot, quad, features, new Vector3(-0.032f, 0f, 0.1f)).transform;
            rightEye = Part("Right Eye", headPivot, quad, features, new Vector3(0.032f, 0f, 0.1f)).transform;
            Part("Brow", headPivot, LatheMesh.Arc("Brow", 0.1f, 0.006f, 0.003f), features, new Vector3(0f, 0.022f, 0.101f));
            upperLip = Part("Upper Lip", headPivot, LatheMesh.Arc("Upper Lip", 0.042f, 0.002f, 0.0035f), features, new Vector3(0f, MouthY, MouthZ)).transform;
            lowerLip = Part("Lower Lip", headPivot, LatheMesh.Arc("Lower Lip", 0.04f, -0.004f, 0.004f), features, new Vector3(0f, MouthY - 0.003f, MouthZ)).transform;
            mouthGlow = Part("Mouth", headPivot, quad, skin, new Vector3(0f, MouthY, MouthZ - 0.002f)).transform;
        }

        private static GameObject Part(string name, Transform parent, Mesh mesh, Material material, Vector3 localPosition)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // A unit quad facing +Z (an eye, the mouth opening); scaled per frame.
        private static Mesh QuadMesh()
        {
            var mesh = new Mesh
            {
                name = "Feature",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                triangles = new[] { 0, 1, 2, 2, 1, 3 }, // clockwise seen from +Z
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
