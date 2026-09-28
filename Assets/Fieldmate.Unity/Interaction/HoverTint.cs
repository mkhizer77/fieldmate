using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// Shows that a part can be grabbed: its colours brighten while a hand or controller hovers it (in reach), and the
    /// hover is logged for device debugging. Uses one MaterialPropertyBlock; a part highlight (assistant or step) that
    /// is pulsing the same renderers simply takes over while it runs.
    /// </summary>
    [RequireComponent(typeof(XRBaseInteractable))]
    public sealed class HoverTint : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private const float Brighten = 0.35f;

        private XRBaseInteractable interactable;
        private MaterialPropertyBlock block;
        private Renderer[] renderers;
        private Color[] originals;

        public bool IsTinted { get; private set; }

        private void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();
            block = new MaterialPropertyBlock();
            renderers = GetComponentsInChildren<Renderer>();
            originals = new Color[renderers.Length];
            for (var i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                originals[i] = material != null && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Color.white;
            }
        }

        private void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }

        private void OnDisable()
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (interactable.interactorsHovering.Count == 1)
            {
                Debug.Log($"[Interaction] hover {name} by {args.interactorObject.transform.name}");
            }

            Apply(true);
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (!interactable.isHovered)
            {
                Apply(false);
            }
        }

        private void Apply(bool tinted)
        {
            IsTinted = tinted;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                if (tinted)
                {
                    block.SetColor(BaseColor, Color.Lerp(originals[i], Color.white, Brighten));
                    renderers[i].SetPropertyBlock(block);
                }
                else
                {
                    renderers[i].SetPropertyBlock(null);
                }
            }
        }
    }
}
