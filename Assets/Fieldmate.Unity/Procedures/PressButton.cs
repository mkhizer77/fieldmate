using System;
using Fieldmate.Interaction;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fieldmate.Procedures
{
    /// <summary>
    /// A physical button on the machine: pinch it (or grip with a controller) to press. The GameObject is unscaled; its
    /// cap is a child (<see cref="CapSize"/>). The label is a small world-space canvas on the cap's front, built at runtime.
    /// Presses are debounced so a flickering pinch doesn't press twice.
    /// </summary>
    public sealed class PressButton : XRSimpleInteractable
    {
        private const float DebounceSeconds = 0.6f;
        public static readonly Vector3 CapSize = new(0.16f, 0.06f, 0.03f);

        [SerializeField] private string label = "Start";

        private Text labelText;
        private AudioSource clickSource;
        private float lastPress = -10f;

        public string Label => labelText != null ? labelText.text : label;

        public event Action Pressed;

        protected override void Awake()
        {
            base.Awake();
            clickSource = InteractionFeedback.CreateSource(gameObject);
            BuildLabel();
        }

        public void SetLabel(string text)
        {
            label = text;
            if (labelText != null)
            {
                labelText.text = text;
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
            InteractionFeedback.Detent(interactorsSelecting, clickSource, strong: true);
            Pressed?.Invoke();
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            Press();
        }

        private void BuildLabel()
        {
            var canvasGo = new GameObject("Label", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, CapSize.z * 0.5f + 0.002f); // just in front of the cap
            canvasGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // text faces +Z (the user)
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(CapSize.x, CapSize.y) * 1000f;
            rect.localScale = Vector3.one * 0.001f;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            labelText = textGo.GetComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 22;
            labelText.fontStyle = FontStyle.Bold;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = Color.white;
            labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            labelText.text = label;
        }
    }
}
