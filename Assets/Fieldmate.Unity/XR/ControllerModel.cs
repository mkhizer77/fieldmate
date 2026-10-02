using System.Threading;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using Material = UnityEngine.Material;

namespace Fieldmate.XR
{
    /// <summary>
    /// The controller as Meta's home shows it (#80): the runtime's own model of the controller in use
    /// (<see cref="RenderModelFeature"/>), built with glTFast under this transform, which follows the controller's aim
    /// pose (device test 2026-10-02: at the grip pose the model sat off the real controller, as Meta's forum reports for
    /// Quest models). Retries until the session runs and the controller is connected. <see cref="Fit"/> is a shared
    /// correction (position in metres, then pitch in degrees, mirrored for the left hand), tuned in the headset with
    /// <see cref="ControllerFit"/> and saved. Materials are URP Lit
    /// with the model's own textures, so no glTFast shader has to ship in the build.
    /// </summary>
    public sealed class ControllerModel : MonoBehaviour
    {
        private const float RetrySeconds = 1f;

        [SerializeField] private bool left;
        [SerializeField] private Vector3 localOffset;
        [SerializeField] private Vector3 localEuler;

        private const string FitKey = "fieldmate.controller.fit";

        /// <summary>Default correction from the aim pose to the model (baked from the device fit when known).</summary>
        public static readonly Vector4 DefaultFit = Vector4.zero;

        /// <summary>The current correction: x, y, z in metres (right hand; mirrored in x for the left), w = pitch degrees.</summary>
        public static Vector4 Fit
        {
            get => fit ??= LoadFit(); // lazily: PlayerPrefs can't be read while Unity loads the type
            set => fit = value;
        }

        private static Vector4? fit;

        private Transform modelRoot;
        private float nextTry;
        private bool loading;
        private GltfImport import;
        private CancellationTokenSource cancel;

        /// <summary>True once the model is built.</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>The runtime's name for the model, e.g. "/model_meta/controller/left".</summary>
        public string ModelName { get; private set; }

        public void Configure(bool leftHand) => left = leftHand;

        private void OnEnable() => cancel = new CancellationTokenSource();

        private void OnDisable() => cancel?.Cancel();

        private void OnDestroy()
        {
            cancel?.Dispose();
            import?.Dispose();
        }

        public static Vector4 LoadFit()
        {
            var saved = PlayerPrefs.GetString(FitKey, string.Empty).Split(',');
            if (saved.Length == 4 && float.TryParse(saved[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                && float.TryParse(saved[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
                && float.TryParse(saved[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)
                && float.TryParse(saved[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
            {
                return new Vector4(x, y, z, w);
            }

            return DefaultFit;
        }

        public static void SaveFit()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(FitKey, string.Format(c, "{0:0.####},{1:0.####},{2:0.####},{3:0.##}", Fit.x, Fit.y, Fit.z, Fit.w));
            PlayerPrefs.Save();
        }

        private void LateUpdate()
        {
            if (modelRoot == null)
            {
                return;
            }

            var fit = Fit;
            modelRoot.localPosition = localOffset + new Vector3(left ? -fit.x : fit.x, fit.y, fit.z);
            modelRoot.localRotation = Quaternion.Euler(localEuler) * Quaternion.Euler(fit.w, 0f, 0f);
        }

        private void Update()
        {
            if (IsLoaded || loading || Time.unscaledTime < nextTry)
            {
                return;
            }

            nextTry = Time.unscaledTime + RetrySeconds;
            if (RenderModelFeature.TryLoadController(left, out var glb, out var name))
            {
                Build(glb, name);
            }
        }

        private async void Build(byte[] glb, string name)
        {
            loading = true;
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                import = new GltfImport(materialGenerator: new LitMaterials(shader));
                if (!await import.Load(glb, null, null, cancel.Token))
                {
                    Debug.LogWarning($"[Presence] controller model {name} could not be read");
                    return;
                }

                var root = new GameObject("Runtime Model").transform;
                root.SetParent(transform, false);
                root.localPosition = localOffset;
                root.localRotation = Quaternion.Euler(localEuler);
                if (!await import.InstantiateMainSceneAsync(root, cancel.Token))
                {
                    Debug.LogWarning($"[Presence] controller model {name} could not be built");
                    return;
                }

                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }

                modelRoot = root;
                ModelName = name;
                IsLoaded = true;
                Debug.Log($"[Presence] controller model {(left ? "left" : "right")}: {name}, {glb.Length / 1024} KB");
            }
            catch (System.OperationCanceledException)
            {
            }
            finally
            {
                loading = false;
            }
        }

        /// <summary>glTF PBR → URP Lit with the model's base colour, texture, metallic and roughness.</summary>
        private sealed class LitMaterials : IMaterialGenerator
        {
            private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
            private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
            private static readonly int Metallic = Shader.PropertyToID("_Metallic");
            private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
            private readonly Shader shader;

            public LitMaterials(Shader lit) => shader = lit;

            public Material GetDefaultMaterial(bool pointsSupport = false) => new(shader) { name = "Controller" };

            public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
            {
                var material = new Material(shader) { name = gltfMaterial.name ?? "Controller" };
                var pbr = gltfMaterial.PbrMetallicRoughness;
                if (pbr == null)
                {
                    return material;
                }

                material.SetColor(BaseColor, pbr.BaseColor.gamma); // glTF factors are linear
                material.SetFloat(Metallic, pbr.metallicFactor);
                material.SetFloat(Smoothness, 1f - pbr.roughnessFactor);
                var texture = pbr.BaseColorTexture;
                if (texture != null && texture.index >= 0)
                {
                    material.SetTexture(BaseMap, gltf.GetTexture(texture.index));
                }

                return material;
            }

            public void SetLogger(ICodeLogger logger)
            {
            }
        }
    }
}
