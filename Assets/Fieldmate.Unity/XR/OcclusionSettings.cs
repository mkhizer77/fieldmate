using System.Collections.Generic;
using Fieldmate.Procedures;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Fieldmate.XR
{
    /// <summary>
    /// Settings → occlusion mode (#6): Hard, Soft or Off, cycled by a button on the machine and kept between sessions.
    /// Environment depth only starts once the scene permission is granted (turning camera-related managers on before
    /// the answer broke passthrough in the platform spike), and only when an occlusion subsystem is loaded: without one
    /// (editor, tests, hardware without depth) AR Foundation throws when the manager is disabled again.
    /// </summary>
    public sealed class OcclusionSettings : MonoBehaviour
    {
        private const string PrefsKey = "fieldmate.occlusion";
        private static readonly List<XROcclusionSubsystem> OcclusionSubsystems = new();
        private static readonly int SoftBlendId = Shader.PropertyToID(OcclusionPlan.SoftBlendProperty);

        [SerializeField] private AROcclusionManager occlusionManager;
        [SerializeField] private ARShaderOcclusion shaderOcclusion;
        [SerializeField] private PressButton button;

        private bool permitted;
        private float nextPermissionCheck;

        public OcclusionMode Mode { get; private set; } = OcclusionMode.Hard;

        /// <summary>True when the XR loader provides environment depth.</summary>
        public static bool DepthAvailable
        {
            get
            {
                SubsystemManager.GetSubsystems(OcclusionSubsystems);
                return OcclusionSubsystems.Count > 0;
            }
        }

        public void Configure(AROcclusionManager manager, ARShaderOcclusion shader, PressButton cycleButton)
        {
            occlusionManager = manager;
            shaderOcclusion = shader;
            button = cycleButton;
        }

        private void Start()
        {
            Mode = (OcclusionMode)Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, (int)OcclusionMode.Hard), 0, 2);
            if (button != null)
            {
                button.Pressed += Cycle;
            }

            Apply();
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.Pressed -= Cycle;
            }
        }

        private void Update()
        {
            // Until the scene permission is answered, check once a second (it's a JNI call on Android).
            if (permitted || Time.unscaledTime < nextPermissionCheck)
            {
                return;
            }

            nextPermissionCheck = Time.unscaledTime + 1f;
            if (QuestPermissions.IsGranted(QuestPermissions.Scene))
            {
                Apply();
            }
        }

        public void Cycle() => SetMode(OcclusionPlan.Next(Mode));

        public void SetMode(OcclusionMode mode)
        {
            Mode = mode;
            PlayerPrefs.SetInt(PrefsKey, (int)mode);
            PlayerPrefs.Save();
            Apply();
            Debug.Log($"[Occlusion] mode {mode}");
        }

        private void Apply()
        {
            permitted = QuestPermissions.IsGranted(QuestPermissions.Scene);
            var plan = OcclusionPlan.For(Mode);
            var on = plan.EnvironmentDepth && permitted && DepthAvailable;
            Shader.SetGlobalFloat(SoftBlendId, plan.SoftBlend);
            if (occlusionManager != null)
            {
                occlusionManager.requestedEnvironmentDepthMode = on ? EnvironmentDepthMode.Fastest : EnvironmentDepthMode.Disabled;
                occlusionManager.enabled = on;
            }

            if (shaderOcclusion != null)
            {
                shaderOcclusion.occlusionShaderMode = on ? plan.ShaderMode : AROcclusionShaderMode.None;
                shaderOcclusion.enabled = on;
            }

            if (button != null)
            {
                button.SetLabel(OcclusionPlan.Label(Mode));
            }
        }
    }
}
