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
    
        [UnityTest]
        public IEnumerator FollowingThePointer_IsSmooth_ATremblingRayDoesNotShakeTheMachine()
        {
            var placement = Object.FindAnyObjectByType<MachinePlacement>();
            Assert.That(placement.State, Is.EqualTo(PlacementState.Placing));
            var pointer = GameObject.Find("Right Pointer").transform;
            var skid = GameObject.Find("PlaceholderSkid").transform;
            pointer.position = new Vector3(0f, 1.3f, 0f);
            var aim = Quaternion.LookRotation(new Vector3(0f, 0f, 1.8f) - pointer.position);
            pointer.rotation = aim;
            yield return new WaitForSeconds(1f);

            // A hand's tremor: ±1° every frame, about ±3.5 cm at the floor 2 m away.
            var min = skid.position;
            var max = skid.position;
            for (var i = 0; i < 40; i++)
            {
                pointer.rotation = aim * Quaternion.Euler(i % 2 == 0 ? 1f : -1f, i % 2 == 0 ? -1f : 1f, 0f);
                yield return null;
                min = Vector3.Min(min, skid.position);
                max = Vector3.Max(max, skid.position);
            }

            Assert.That(Vector3.Distance(min, max), Is.LessThan(0.02f), "device test 2026-10-01: eased, not jittering with the ray");
        }
    }
}
