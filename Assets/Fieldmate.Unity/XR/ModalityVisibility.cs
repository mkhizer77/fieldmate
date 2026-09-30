using UnityEngine;

namespace Fieldmate.XR
{
    /// <summary>Shows a visual only while the user is on the given input (controller models only when controllers are tracked).</summary>
    public sealed class ModalityVisibility : MonoBehaviour
    {
        [SerializeField] private Modality shownFor = Modality.Controllers;
        [SerializeField] private GameObject visual;

        public Modality ShownFor => shownFor;

        public void Configure(Modality modality, GameObject target)
        {
            shownFor = modality;
            visual = target;
        }

        private void OnEnable()
        {
            InputModalityProbe.Changed += Apply;
            Apply(InputModalityProbe.Current);
        }

        private void OnDisable() => InputModalityProbe.Changed -= Apply; // static event: always unsubscribe

        private void Apply(Modality modality)
        {
            if (visual != null)
            {
                visual.SetActive(modality == shownFor);
            }
        }
    }
}
