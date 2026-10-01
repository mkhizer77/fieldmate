using Fieldmate.AI;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// The assistant as a hologram (#69): a small half-body figure (0.42x life size, #71) projected from a floating puck
    /// that the user puts where they want it during setup, in place of the chat panel. It materialises top-down when it
    /// first appears, breathes, blinks and looks at the user; it brightens while listening, its scanlines race while it
    /// thinks, and its lips move with the speech that is actually playing (<see cref="LipSync"/> on the player's output).
    /// Built in code from lathe meshes (no downloaded or paid assets); a modelled figure can replace <see cref="Build"/>
    /// later behind the same component. No per-frame allocations.
    /// </summary>
    public sealed class HologramMate : MonoBehaviour
    {
        /// <summary>Height of the figure's waist above the puck (the component's origin).</summary>
        public const float WaistHeight = 0.05f;

        /// <summary>Small enough to sit on a table or float beside the user (device test 2026-10-01: 0.88 loomed).</summary>
        public const float FigureScale = 0.42f;

        /// <summary>Eye line above the puck, in metres.</summary>
        public const float EyeHeight = WaistHeight + HeadHeight * FigureScale;

        private const float MaterialiseSeconds = 1.4f;

        private const float HeadHeight = 0.745f; // eye line, figure space
        private const float MaxHeadYaw = 40f;
        private const float MaxHeadPitch = 30f; // it usually sits below the user's eyes and looks up

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int ScanSpeedId = Shader.PropertyToID("_ScanSpeed");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        private static readonly int FadeRangeId = Shader.PropertyToID("_FadeRange");
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int DepthWriteId = Shader.PropertyToID("_DepthWrite");
        private static readonly int ScanDensityId = Shader.PropertyToID("_ScanDensity");
        private static readonly int GlitchId = Shader.PropertyToID("_Glitch");

        private static readonly Color Cyan = new(0.25f, 0.85f, 1f, 1f);
        private static readonly Color ListenTint = new(0.35f, 1f, 0.8f, 1f);
        private static readonly Color ThinkTint = new(0.45f, 0.7f, 1f, 1f);

        [SerializeField] private Transform head;
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
        private Material beamMaterial;
        private AssistantState state;
        private float intensity = 1f;
        private float scanSpeed = 0.12f;
        private Color tint = Cyan;
        private float nextBlink = 3f;
        private float blinkUntil;
        private float yaw;
        private float presence = 1f; // 0 hidden .. 1 fully there
        private float presenceTarget = 1f;
        private Renderer[] renderers;
        private bool renderersOn = true;
        private Material lensMaterial;

        public AssistantState State => state;

        /// <summary>Mouth openness 0..1 this frame.</summary>
        public float MouthOpen => lips.Open;

        /// <summary>Distance the lower lip has dropped from closed, in figure space (tests).</summary>
        public float LipGap => upperLip != null ? upperLip.localPosition.y - lowerLip.localPosition.y : 0f;

        public Transform Figure => figure;
        public Transform HeadPivot => headPivot;
        public bool IsBlinking => Time.time < blinkUntil;

        /// <summary>True while any part of the figure is drawn.</summary>
        public bool IsVisible => renderersOn;

        /// <summary>0 hidden .. 1 fully materialised.</summary>
        public float Presence => presence;

        public void Configure(Transform headTransform, StreamingAudioPlayer speech)
        {
            head = headTransform;
            player = speech;
        }

        /// <summary>Hidden at once (setup's room scan) or materialising / dissolving over a second and a half.</summary>
        public void SetVisible(bool visible, bool instant = false)
        {
            presenceTarget = visible ? 1f : 0f;
            if (instant)
            {
                presence = presenceTarget;
                ApplyPresence();
            }
        }

        /// <summary>Puts the puck at <paramref name="position"/> (setup placement); the figure keeps facing the user.</summary>
        public void Place(Vector3 position) => transform.position = position;

        public void SetHead(Transform headTransform) => head = headTransform;

        /// <summary>Another speech source than the scene's player (tests).</summary>
        public void UseSpeech(ISpeechOutput output) => speechOverride = output;

        private ISpeechOutput Speech => speechOverride ?? player;

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
            if (!Mathf.Approximately(presence, presenceTarget))
            {
                presence = Mathf.MoveTowards(presence, presenceTarget, dt / MaterialiseSeconds);
                ApplyPresence();
            }

            if (!renderersOn)
            {
                return;
            }

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
            figure.localPosition = new Vector3(0f, WaistHeight + 0.004f * Mathf.Sin(now * 0.9f), 0f);
            // The waist dissolves into the beam; while materialising the cut sweeps down from above the head.
            var waist = figure.position.y - 0.005f;
            skin.SetFloat(FadeStartId, waist + (1f - presence) * (EyeHeight + 0.1f));
            beamMaterial.SetFloat(FadeStartId, transform.position.y + 0.12f);

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
            skin.SetFloat(IntensityId, intensity * presence);
            skin.SetFloat(ScanSpeedId, scanSpeed);
            skin.SetColor(BaseColorId, tint);
            features.SetFloat(IntensityId, (1.6f + 0.6f * open) * presence);
            lensMaterial.SetFloat(IntensityId, 1.6f * Mathf.Max(presence, 0.35f));
            beamMaterial.SetFloat(IntensityId, 0.5f * presence);
            features.SetColor(BaseColorId, tint);

            ring.localRotation = Quaternion.Euler(0f, now * (state is AssistantState.Thinking or AssistantState.Transcribing ? 160f : 25f), 0f);
        }

        private void ApplyPresence()
        {
            var on = presence > 0.001f;
            if (on == renderersOn)
            {
                return;
            }

            renderersOn = on;
            foreach (var r in renderers)
            {
                r.enabled = on;
            }
        }

        private void LateUpdate()
        {
            if (head == null || !renderersOn)
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
            skin.SetFloat(FadeRangeId, 0.1f);
            skin.SetFloat(ScanDensityId, 60f); // the same number of lines across a smaller figure
            skin.SetFloat(GlitchId, 0.15f);
            features = new Material(skin) { name = "Hologram Features" };
            features.SetFloat(FillId, 0.9f);
            features.SetFloat(FadeStartId, -10000f); // eyes, lips, lens and ring never fade

            // Projector: a small dark puck (solid, occluded like the machine) with a glowing lens and a slow ring.
            var solid = Shader.Find("Fieldmate/OccludedLit");
            var steel = new Material(solid != null ? solid : Shader.Find("Universal Render Pipeline/Lit")) { name = "Projector" };
            steel.SetColor(BaseColorId, new Color(0.13f, 0.14f, 0.16f));
            Part("Projector", transform, LatheMesh.Build("Projector", new[]
            {
                new Vector2(0f, -0.022f), new Vector2(0.05f, -0.02f), new Vector2(0.075f, -0.008f), new Vector2(0.078f, 0f),
                new Vector2(0f, 0.001f),
            }, 32), steel, Vector3.zero);
            lensMaterial = new Material(features) { name = "Projector Lens" };
            Part("Lens", transform, LatheMesh.Build("Lens", new[]
            {
                new Vector2(0.055f, 0f), new Vector2(0.055f, 0.003f), new Vector2(0f, 0.004f),
            }, 32), lensMaterial, new Vector3(0f, 0.001f, 0f));
            ring = Part("Ring", transform, LatheMesh.Build("Ring", new[]
            {
                new Vector2(0.09f, 0f), new Vector2(0.093f, 0.003f), new Vector2(0.09f, 0.006f), new Vector2(0.087f, 0.003f), new Vector2(0.09f, 0f),
            }, 48), features, new Vector3(0f, 0.004f, 0f)).transform;
            // The projector's beam: a faint open cone from the lens up past the waist, all rim and no fill.
            var beam = new Material(skin) { name = "Hologram Beam" };
            beam.SetFloat(FillId, 0.02f);
            beam.SetFloat(IntensityId, 0.5f);
            beam.SetFloat(DepthWriteId, 0f); // light, not a surface: it must not hide the waist behind it
            beamMaterial = beam;
            beam.SetFloat(FadeRangeId, -0.1f);
            Part("Beam", transform, LatheMesh.Build("Beam", new[]
            {
                new Vector2(0.05f, 0f), new Vector2(0.085f, 0.11f),
            }, 40), beam, new Vector3(0f, 0.004f, 0f));

            figure = new GameObject("Figure").transform;
            figure.SetParent(transform, false);
            figure.localPosition = new Vector3(0f, WaistHeight, 0f);
            figure.localScale = Vector3.one * FigureScale;

            // Torso from the waist (faded into the beam) to the neck; elliptical, shoulders wider than deep.
            body = new GameObject("Body").transform;
            body.SetParent(figure, false);
            Part("Torso", body, LatheMesh.Build("Torso", new[]
            {
                new Vector2(0.13f, 0f), new Vector2(0.14f, 0.08f), new Vector2(0.155f, 0.18f), new Vector2(0.175f, 0.28f),
                new Vector2(0.19f, 0.36f), new Vector2(0.19f, 0.42f), new Vector2(0.165f, 0.47f), new Vector2(0.11f, 0.505f),
                new Vector2(0.055f, 0.52f), new Vector2(0.046f, 0.56f), new Vector2(0.04f, 0.61f), new Vector2(0.036f, 0.645f),
                new Vector2(0f, 0.648f), // the neck runs up under the jaw; the depth pass hides the inside
            }, 40, depth: 0.58f), skin, Vector3.zero);
            foreach (var side in new[] { -1f, 1f })
            {
                // Upper arms hang from the shoulders, slightly away from the body; the beam fades them at the elbow.
                // The rounded top (deltoid) sits inside the shoulder line so the arm grows out of the body.
                var arm = Part(side < 0f ? "Left Arm" : "Right Arm", body, LatheMesh.Build("Arm", new[]
                {
                    new Vector2(0f, -0.32f), new Vector2(0.03f, -0.31f), new Vector2(0.04f, -0.25f), new Vector2(0.046f, -0.12f),
                    new Vector2(0.054f, -0.03f), new Vector2(0.056f, 0.01f), new Vector2(0.046f, 0.045f), new Vector2(0.02f, 0.062f),
                    new Vector2(0f, 0.065f),
                }, 24, depth: 0.9f), skin, new Vector3(side * 0.172f, 0.43f, 0f));
                arm.transform.localRotation = Quaternion.Euler(0f, 0f, side * 4f);
            }

            headPivot = new GameObject("Head").transform;
            headPivot.SetParent(figure, false);
            headPivot.localPosition = new Vector3(0f, HeadHeight, 0.01f);
            Part("Skull", headPivot, LatheMesh.Build("Head", new[]
            {
                new Vector2(0f, -0.125f), new Vector2(0.038f, -0.12f), new Vector2(0.06f, -0.105f), new Vector2(0.072f, -0.085f),
                new Vector2(0.079f, -0.05f), new Vector2(0.085f, -0.01f), new Vector2(0.088f, 0.03f), new Vector2(0.085f, 0.07f),
                new Vector2(0.073f, 0.1f), new Vector2(0.05f, 0.122f), new Vector2(0.02f, 0.132f), new Vector2(0f, 0.134f),
            }, 36, depth: 1.12f), skin, Vector3.zero);
            // Hair: a slightly larger cap over the crown and the back of the head gives the figure a character silhouette.
            var hair = Part("Hair", headPivot, LatheMesh.Build("Hair", new[]
            {
                new Vector2(0.086f, 0.045f), new Vector2(0.092f, 0.065f), new Vector2(0.091f, 0.09f), new Vector2(0.08f, 0.115f),
                new Vector2(0.058f, 0.135f), new Vector2(0.03f, 0.145f), new Vector2(0f, 0.147f),
            }, 36, depth: 1.13f), skin, new Vector3(0f, 0f, -0.006f));
            hair.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f); // swept back: the hairline rises off the forehead

            // Face: glowing eyes and lips, drawn a hair in front of the skull so they read from any angle.
            var quad = DiscMesh();
            leftEye = Part("Left Eye", headPivot, quad, features, new Vector3(-0.032f, 0f, 0.1f)).transform;
            rightEye = Part("Right Eye", headPivot, quad, features, new Vector3(0.032f, 0f, 0.1f)).transform;
            Part("Brow", headPivot, LatheMesh.Arc("Brow", 0.1f, 0.006f, 0.003f), features, new Vector3(0f, 0.022f, 0.101f));
            upperLip = Part("Upper Lip", headPivot, LatheMesh.Arc("Upper Lip", 0.042f, 0.002f, 0.0035f), features, new Vector3(0f, MouthY, MouthZ)).transform;
            lowerLip = Part("Lower Lip", headPivot, LatheMesh.Arc("Lower Lip", 0.04f, -0.004f, 0.004f), features, new Vector3(0f, MouthY - 0.003f, MouthZ)).transform;
            mouthGlow = Part("Mouth", headPivot, quad, skin, new Vector3(0f, MouthY, MouthZ - 0.002f)).transform;
            renderers = GetComponentsInChildren<Renderer>();
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

        // A unit-diameter disc facing +Z (an eye, the mouth opening); scaled per frame into an ellipse.
        private static Mesh DiscMesh(int segments = 20)
        {
            var vertices = new Vector3[segments + 1];
            var normals = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            normals[0] = Vector3.forward;
            for (var i = 0; i < segments; i++)
            {
                var a = i * Mathf.PI * 2f / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f);
                normals[i + 1] = Vector3.forward;
                // Seen from +Z the x axis is mirrored, so rising angles run clockwise on screen: Unity's front face.
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }

            var mesh = new Mesh { name = "Feature", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
