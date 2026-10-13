using System;
using Fieldmate.Procedures;
using UnityEngine;

namespace Fieldmate.Vision
{
    /// <summary>
    /// Settings → local-only mode (design.md §5.5 privacy model, #23): when on, the camera is never read and no image
    /// leaves the headset; "What's this?" answers from the twin's own data instead. Toggled by the Camera button in the
    /// hand menu and kept between sessions. Voice still goes to the cloud; the README's data-flow section says so.
    /// </summary>
    public sealed class PrivacySettings : MonoBehaviour
    {
        public const string PrefsKey = "fieldmate.localOnly";

        [SerializeField] private PressButton button;

        /// <summary>Raised when local-only mode changes (true = on).</summary>
        public static event Action<bool> Changed;

        /// <summary>Read on every capture, so no component has to exist for the rule to hold.</summary>
        public static bool LocalOnly
        {
            get => PlayerPrefs.GetInt(PrefsKey, 0) == 1;
            set
            {
                if (value == LocalOnly)
                {
                    return;
                }

                PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Debug.Log($"[Privacy] local-only mode {(value ? "on: camera off" : "off")}");
                Changed?.Invoke(value);
            }
        }

        public static string ButtonLabel(bool localOnly) => localOnly ? "Camera: Off (local-only)" : "Camera: On";

        public void Configure(PressButton toggle) => button = toggle;

        private void Start()
        {
            Changed += OnChanged;
            if (button != null)
            {
                button.Pressed += Toggle;
                button.SetLabel(ButtonLabel(LocalOnly));
            }
        }

        private void OnDestroy()
        {
            Changed -= OnChanged; // static event: unsubscribe first (Known pitfalls)
            if (button != null)
            {
                button.Pressed -= Toggle;
            }
        }

        public void Toggle() => LocalOnly = !LocalOnly;

        private void OnChanged(bool localOnly)
        {
            if (button != null)
            {
                button.SetLabel(ButtonLabel(localOnly));
            }
        }
    }
}
