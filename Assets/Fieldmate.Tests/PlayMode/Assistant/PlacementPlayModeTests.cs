using System.Collections;
using Fieldmate.Assistant;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant
{
    public class PlacementPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator LoadBench()
        {
            PlayerPrefs.DeleteKey("fieldmate.machine.anchor");
            yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator WithoutASavedAnchor_StartsInPlacementMode()
        {
            var placement = Object.FindAnyObjectByType<MachinePlacement>();

            Assert.That(placement.State, Is.EqualTo(PlacementState.Placing));
            yield break;
        }

        [UnityTest]
        public IEnumerator Confirm_PlacesTheMachine_EvenWithoutAnchorSupport()
        {
            var placement = Object.FindAnyObjectByType<MachinePlacement>();
            var target = new Pose(new Vector3(1f, 0f, 2.5f), Quaternion.Euler(0f, 30f, 0f));

            var confirm = placement.ConfirmAsync(target);
            yield return new WaitUntil(() => confirm.IsCompleted);

            Assert.That(placement.State, Is.EqualTo(PlacementState.Placed));
            var machine = Object.FindAnyObjectByType<MachineServices>();
            Assert.That(machine.Parts, Has.Count.EqualTo(14));
            var skid = GameObject.Find("PlaceholderSkid").transform;
            Assert.That(Vector3.Distance(skid.position, target.position), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(skid.rotation, target.rotation), Is.LessThan(0.1f));
        }
    }
}
