using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// Detent feedback: a haptic pulse on every selecting controller, plus a short click from the part itself so hands
    /// (no haptics) get feedback too. The click clip is generated once; playback reuses one AudioSource per part.
    /// </summary>
    public static class InteractionFeedback
    {
        private const int ClickRate = 22050;
        private static AudioClip click;

        public static void Detent(IReadOnlyList<IXRSelectInteractor> interactors, AudioSource source, bool strong)
        {
            for (var i = 0; i < interactors.Count; i++)
            {
                if (interactors[i] is XRBaseInputInteractor input)
                {
                    input.SendHapticImpulse(strong ? 0.7f : 0.25f, strong ? 0.08f : 0.03f);
                }
            }

            if (source != null)
            {
                source.PlayOneShot(Click, strong ? 0.8f : 0.35f);
            }
        }

        /// <summary>A long, firm pulse: the part is held by a safety interlock and did not move.</summary>
        public static void Refused(IReadOnlyList<IXRSelectInteractor> interactors)
        {
            for (var i = 0; i < interactors.Count; i++)
            {
                if (interactors[i] is XRBaseInputInteractor input)
                {
                    input.SendHapticImpulse(1f, 0.25f);
                }
            }
        }

        /// <summary>A quiet spatial AudioSource for part clicks.</summary>
        public static AudioSource CreateSource(GameObject owner)
        {
            var source = owner.GetComponent<AudioSource>();
            if (source == null)
            {
                source = owner.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 0.3f;
            source.maxDistance = 5f;
            return source;
        }

        private static AudioClip Click
        {
            get
            {
                if (click != null)
                {
                    return click;
                }

                var samples = new float[ClickRate / 40]; // 25 ms decaying tick
                for (var i = 0; i < samples.Length; i++)
                {
                    var t = (float)i / samples.Length;
                    samples[i] = Mathf.Sin(i * 0.9f) * (1f - t) * (1f - t);
                }

                click = AudioClip.Create("Detent click", samples.Length, 1, ClickRate, false);
                click.SetData(samples, 0);
                return click;
            }
        }
    }
}
