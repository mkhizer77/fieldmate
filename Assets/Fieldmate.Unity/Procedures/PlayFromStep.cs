using System.Collections;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.XR;
using UnityEngine;

namespace Fieldmate.Procedures
{
    /// <summary>
    /// Play-from-step for fast iteration (#11): the Procedure Editor stores a step id and enters Play mode; this skips the
    /// guided setup, confirms the machine where it stands and advances the procedure to that step with the same events a
    /// user would cause (the runner's state, not the controls' poses). The request is consumed, so the next Play is
    /// normal. Editor only in practice: nothing sets the key in a player build.
    /// </summary>
    public sealed class PlayFromStep : MonoBehaviour
    {
        public const string PrefsKey = "fieldmate.editor.playFromStep";

        private string stepId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeScene()
        {
            if (Application.isEditor && PlayerPrefs.HasKey(PrefsKey))
            {
                SetupFlow.AutoRun = false; // read in Awake: decide before the scene's objects wake
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterScene()
        {
            if (!Application.isEditor || !PlayerPrefs.HasKey(PrefsKey))
            {
                return;
            }

            var step = PlayerPrefs.GetString(PrefsKey);
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            Run(step);
        }

        /// <summary>Advances the loaded scene's procedure to <paramref name="step"/> (also used by tests).</summary>
        public static PlayFromStep Run(string step)
        {
            var go = new GameObject("Play From Step", typeof(PlayFromStep));
            var runner = go.GetComponent<PlayFromStep>();
            runner.stepId = step;
            return runner;
        }

        private IEnumerator Start()
        {
            var machine = FindAnyObjectByType<MachineServices>();
            var placement = FindAnyObjectByType<MachinePlacement>();
            if (machine == null || placement == null)
            {
                yield break;
            }

            yield return new WaitUntil(() => placement.State != PlacementState.Loading);
            if (placement.State != PlacementState.Placed)
            {
                var root = placement.transform; // the machine is wherever the scene put it
                var skid = FindAnyObjectByType<Fieldmate.Interaction.RemovablePart>()?.transform.root ?? root;
                _ = placement.ConfirmAsync(new Pose(skid.position, skid.rotation));
                yield return new WaitUntil(() => placement.State == PlacementState.Placed);
            }

            var clock = machine.Now;
            ProcedureReplay.AdvanceTo(machine.Runner, stepId, ref clock);
            Debug.Log($"[Procedure] play from step: {stepId} ({machine.Runner.CurrentStepIndex + 1} of {machine.Runner.Definition.Steps.Count})");
            Destroy(gameObject);
        }
    }
}
