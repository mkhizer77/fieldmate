using System;
using Fieldmate.Interaction;
using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fieldmate.Procedures
{
    public enum ButtonStyle
    {
        /// <summary>The one action to take next (accent cap).</summary>
        Primary,

        /// <summary>Available, not the focus (dark cap).</summary>
        Secondary,

        /// <summary>Not available yet (dimmed).</summary>
        Muted,
    }

    /// <summary>
    /// A physical button on the machine: pinch it (or press trigger with a controller) to press. Builds its own visuals:
    /// a dark bezel, a cap that depresses on press, and a label. Presses are debounced so a flickering pinch doesn't
    /// press twice. The GameObject is unscaled; the cap is a child (<see cref="CapSize"/>).
    /// </summary>
    [RequireComponent(typeof(HoverTint))]
    public sealed class PressButton : XRSimpleInteractable
    {
        private const float DebounceSeconds = 0.6f;
        private const float PressDepth = 0.012f;
        private const float PressSeconds = 0.09f;
        public static readonly Vector3 CapSize = new(0.16f, 0.06f, 0.03f);
        private static readonly Vector3 BezelSize = new(0.176f, 0.076f, 0.02f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        private static Material bezelMaterial;
        private static Material[] capMaterials;

        [SerializeField] private string label = "Start";
        [SerializeField] private ButtonStyle style = ButtonStyle.Secondary;

        private TMP_Text labelText;
        private Transform cap;
        private Renderer capRenderer;
        private AudioSource clickSource;
        private float lastPress = -10f;
        private float pressedAt = -10f;
        private bool animating;

        public string Label => labelText != null ? labelText.text : label;
        public ButtonStyle Style => style;

        /// <summary>How far the cap is pressed in (metres); 0 at rest.</summary>
        public float CapDepth => cap != null ? -cap.localPosition.z : 0f;

        public event Action Pressed;

        protected override void Awake()
        {
            BuildVisuals(); // before base.Awake, which collects the cap's collider
            base.Awake();
            clickSource = InteractionFeedback.CreateSource(gameObject);
            ApplyStyle();
        }

        public void SetLabel(string text)
        {
            label = text;
            if (labelText != null)
            {
                labelText.text = text;
            }
        }

        public void SetStyle(ButtonStyle buttonStyle)
        {
            style = buttonStyle;
            if (capRenderer != null)
            {
                ApplyStyle();
            }
        }

        /// <summary>Presses it as if by hand (tests, debug).</summary>
        public void Press()
        {
            if (Time.unscaledTime - lastPress < DebounceSeconds)
            {
                return;
            }

            lastPress = Time.unscaledTime;
            pressedAt = Time.unscaledTime;
            animating = true;
            InteractionFeedback.Detent(interactorsSelecting, clickSource, strong: true);
            Pressed?.Invoke();
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            Press();
        }

        private void Update()
        {
            if (!animating)
            {
                return;
            }

            // In over PressSeconds, back out over the same time.
            var t = (Time.unscaledTime - pressedAt) / PressSeconds;
            var depth = t < 1f ? t : t < 2f ? 2f - t : 0f;
            cap.localPosition = new Vector3(0f, 0f, -PressDepth * depth);
            animating = t < 2f;
        }

        private void ApplyStyle()
        {
            capRenderer.sharedMaterial = CapMaterials[(int)style];
            labelText.color = style switch
            {
                ButtonStyle.Primary => Theme.TextOnAccent,
                ButtonStyle.Secondary => Theme.TextPrimary,
                _ => Theme.TextMuted,
            };
        }

        private void BuildVisuals()
        {
            var bezel = Block("Bezel", BezelSize, new Vector3(0f, 0f, -(CapSize.z + BezelSize.z) * 0.5f + 0.004f), collider: false);
            bezel.GetComponent<Renderer>().sharedMaterial = BezelMaterial;
            cap = Block("Cap", CapSize, Vector3.zero, collider: true);
            capRenderer = cap.GetComponent<Renderer>();

            var canvasGo = new GameObject("Label", typeof(RectTransform));
            canvasGo.transform.SetParent(cap, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, 0.5f + 0.002f / CapSize.z); // just in front of the cap face
            canvasGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // text faces +Z (the user)
            canvasGo.transform.localScale = new Vector3(1f / CapSize.x, 1f / CapSize.y, 1f / CapSize.z); // undo the cap's scale
            var inner = new GameObject("Canvas", typeof(RectTransform));
            inner.transform.SetParent(canvasGo.transform, false);
            UiKit.WorldCanvas(inner, CapSize.x * 1000f, CapSize.y * 1000f);
            labelText = UiKit.Label("Text", inner.transform, Vector2.zero, Vector2.one, Theme.Body - 2f, Theme.TextPrimary,
                TextAlignmentOptions.Center, semiBold: true, new Vector2(8f, 0f), new Vector2(-8f, 0f));
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.enableAutoSizing = true; // "Confirm restart" and "Occlusion: Hard" shrink to fit the cap
            labelText.fontSizeMin = 15f;
            labelText.fontSizeMax = Theme.Body - 2f;
            labelText.text = label;
        }

        private Transform Block(string name, Vector3 size, Vector3 localPosition, bool collider)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (collider)
            {
                go.AddComponent<BoxCollider>();
            }

            return go.transform;
        }

        private static Material BezelMaterial => bezelMaterial != null ? bezelMaterial : bezelMaterial = Make("Button Bezel", Theme.ButtonBezel, 0.3f);

        private static Material[] CapMaterials => capMaterials ??= new[]
        {
            Make("Button Primary", Theme.ButtonPrimary, 0.55f),
            Make("Button Secondary", Theme.ButtonSecondary, 0.45f),
            Make("Button Muted", Theme.ButtonMuted, 0.3f),
        };

        // Same lit shader as the machine so real furniture occludes the buttons too; URP Lit when it is missing (tests).
        private static Material Make(string name, Color color, float smoothness)
        {
            var shader = Shader.Find("Fieldmate/OccludedLit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            var material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor(BaseColor, color);
            material.SetFloat(Smoothness, smoothness);
            return material;
        }
    }
}
