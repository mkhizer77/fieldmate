using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Procedures;
using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fieldmate.XR
{
    /// <summary>
    /// The app's controls in a hand menu on the left hand (#73), so nothing sits on the machine and nothing floats in
    /// view until wanted: Start / Restart, Move machine, Occlusion, Stats and Labels in one column of flat rows.
    /// With hands, turning the left palm towards the face opens it beside the palm (Microsoft's hand-menu pattern); when
    /// the palm turns away it stays world-locked for a moment so the right hand's ray can still press a row. With
    /// controllers, the left menu button toggles it above the controller. Closed during the guided setup. The rows are
    /// <see cref="PressButton"/>s that the director, placement, occlusion and stats already listen to.
    /// </summary>
    public sealed class HandMenu : MonoBehaviour
    {
        public const float RowPitch = 0.038f;
        private const float WidthMm = 150f;
        private const float HeaderMm = 34f;
        private const float OpenDot = 0.55f;
        private const float StayDot = 0.35f;
        private const float LingerSeconds = 1.6f;
        private const float GrowSeconds = 0.12f;

        [SerializeField] private Transform head;
        [SerializeField] private PalmPose palm;
        [SerializeField] private Transform leftController;
        [SerializeField] private PressButton labelsButton;
        [SerializeField] private SetupFlow setup;

        private readonly HiddenObjects hidden = new();
        private InputAction menuAction;
        private bool labelsShown = true;
        private Transform panel;
        private bool open;
        private bool toggled; // controllers: opened by the menu button
        private float closeAt = -1f;
        private float grow;

        public bool IsOpen => open;

        /// <summary>Y of a row's centre in the menu, rows counted from the top.</summary>
        public static float RowY(int row) => -(HeaderMm * 0.001f + RowPitch * 0.5f + row * RowPitch);

        public void Configure(Transform headTransform, PalmPose leftPalm, Transform leftControllerPointer, PressButton labels)
        {
            head = headTransform;
            palm = leftPalm;
            leftController = leftControllerPointer;
            labelsButton = labels;
        }

        private void Awake()
        {
            menuAction = new InputAction("Menu", InputActionType.Button, "<XRController>{LeftHand}/{MenuButton}");
            menuAction.AddBinding("<Keyboard>/tab");
            menuAction.performed += _ => Toggle();
            Build();
        }

        // After every row has built its visuals in its own Awake, so hiding catches them all.
        private void Start() => SetOpen(false, instant: true);

        private void OnEnable()
        {
            menuAction.Enable();
            if (labelsButton != null)
            {
                labelsButton.Pressed += ToggleLabels;
            }
        }

        private void OnDisable()
        {
            menuAction.Disable();
            if (labelsButton != null)
            {
                labelsButton.Pressed -= ToggleLabels;
            }
        }

        private void OnDestroy() => menuAction.Dispose();

        /// <summary>Opens or closes it (the controller's menu button; tests).</summary>
        public void Toggle()
        {
            if (!Available)
            {
                return;
            }

            toggled = !open;
            SetOpen(!open);
        }

        private bool Available => setup == null || setup.Stage == Fieldmate.AI.SetupStage.Done;

        private void ToggleLabels() => ControlTag.ShowAll = !ControlTag.ShowAll;

        private void Update()
        {
            if (labelsButton != null && labelsShown != ControlTag.ShowAll)
            {
                labelsShown = ControlTag.ShowAll;
                labelsButton.SetLabel(labelsShown ? "Labels: On" : "Labels: Off");
            }

            if (!Available)
            {
                if (open)
                {
                    SetOpen(false);
                }

                return;
            }

            // Hands: the palm towards the face opens it; turning away keeps it a moment, world-locked.
            if (!toggled && palm != null && head != null)
            {
                var facing = palm.IsTracked ? Vector3.Dot(palm.PalmNormal, (head.position - palm.transform.position).normalized) : -1f;
                if (facing > (open ? StayDot : OpenDot))
                {
                    closeAt = -1f;
                    if (!open)
                    {
                        SetOpen(true);
                    }

                    FollowPalm();
                }
                else if (open)
                {
                    if (closeAt < 0f)
                    {
                        closeAt = Time.time + LingerSeconds;
                    }
                    else if (Time.time >= closeAt)
                    {
                        SetOpen(false);
                    }
                }
            }
            else if (toggled && open)
            {
                FollowController();
            }

            if (open && grow < 1f)
            {
                grow = Mathf.MoveTowards(grow, 1f, Time.deltaTime / GrowSeconds);
                transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, grow);
            }
        }

        // Beside the palm, towards the body's centre so the other hand reaches it, turned to the face.
        private void FollowPalm()
        {
            var right = Flat(head.right);
            transform.position = palm.transform.position + right * 0.11f + Vector3.up * 0.07f;
            UiKit.FaceAway(transform, head.position);
        }

        private void FollowController()
        {
            if (leftController == null)
            {
                return;
            }

            var right = Flat(head.right);
            transform.position = leftController.position + Vector3.up * 0.12f + right * 0.04f;
            UiKit.FaceAway(transform, head.position);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.right;
        }

        private void SetOpen(bool value, bool instant = false)
        {
            open = value;
            if (!value)
            {
                toggled = false;
                closeAt = -1f;
            }

            grow = instant || !value ? 1f : 0f;
            transform.localScale = Vector3.one * (value && !instant ? 0.85f : 1f); // the whole menu: the card keeps its mm scale
            // Hidden, not deactivated: the rows stay registered and their listeners keep working.
            if (value)
            {
                hidden.Show();
            }
            else
            {
                hidden.Hide(transform);
            }

            if (value)
            {
                Debug.Log($"[Menu] open ({(toggled ? "menu button" : "palm up")})");
            }
        }

        // The card behind the rows, with a small header. The rows themselves are children placed by the scene builder.
        private void Build()
        {
            var rows = 0;
            foreach (Transform child in transform)
            {
                if (child.GetComponent<PressButton>() != null)
                {
                    rows++;
                }
            }

            var heightMm = HeaderMm + rows * RowPitch * 1000f + 8f;
            var canvasGo = new GameObject("Menu Card", typeof(RectTransform));
            panel = canvasGo.transform;
            panel.SetParent(transform, false);
            UiKit.WorldCanvas(canvasGo, WidthMm, heightMm, PressButton.RowUnitsPerMm);
            var rect = (RectTransform)panel;
            rect.pivot = new Vector2(0.5f, 1f); // the top edge at the menu's origin; rows hang below it
            panel.localPosition = Vector3.zero;
            UiKit.Card("Background", panel, Vector2.zero, Vector2.one, Theme.Surface, stroke: true);
            var eyebrow = UiKit.Eyebrow("Title", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), Theme.TextMuted, TextAlignmentOptions.Center);
            eyebrow.rectTransform.offsetMin = new Vector2(0f, -HeaderMm * PressButton.RowUnitsPerMm);
            eyebrow.rectTransform.offsetMax = new Vector2(0f, -4f * PressButton.RowUnitsPerMm);
            eyebrow.text = "Fieldmate";
        }
    }
}
