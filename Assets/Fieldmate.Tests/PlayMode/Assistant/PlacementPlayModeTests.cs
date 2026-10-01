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
        public IEnumerator AutoFit_ShrinksTheMachineBetweenCloseWalls_AndGrowsItBackInTheOpen()
        {
            var placement = Object.FindAnyObjectByType<MachinePlacement>();
            Assert.That(placement.State, Is.EqualTo(PlacementState.Placing));
            var pointer = GameObject.Find("Right Pointer").transform;
            var target = new Vector3(0f, 0f, 1.8f);
            pointer.position = new Vector3(0f, 1.3f, 0f);
            pointer.rotation = Quaternion.LookRotation(target - pointer.position);

            // A room 3 m wide and 3 m deep around the spot: the cabinet end needs to shrink to keep 35 cm to its wall.
            var walls = new GameObject("Test walls");
            foreach (var (centre, size) in new[]
                     {
                         (new Vector3(-1.5f, 1f, 1.8f), new Vector3(0.1f, 2f, 4f)), (new Vector3(1.5f, 1f, 1.8f), new Vector3(0.1f, 2f, 4f)),
                         (new Vector3(0f, 1f, 3.3f), new Vector3(4f, 2f, 0.1f)), (new Vector3(0f, 1f, 0.3f), new Vector3(4f, 2f, 0.1f)),
                     })
            {
                var wall = new GameObject("Wall", typeof(BoxCollider));
                wall.transform.SetParent(walls.transform);
                wall.transform.position = centre;
                wall.GetComponent<BoxCollider>().size = size;
            }

            yield return new WaitForSeconds(1.5f);
            Assert.That(placement.FitScale, Is.InRange(0.7f, 0.85f), "#57: 1.5 m to the side walls → about 80 %");
            Assert.That(placement.Scale, Is.EqualTo(placement.FitScale).Within(0.01f), "the machine eases to the fitted size");
            Assert.That(placement.HintText, Does.Contain("% to fit the space"));

            Object.Destroy(walls);
            yield return new WaitForSeconds(1.5f);
            Assert.That(placement.FitScale, Is.EqualTo(1f), "nothing near: full size");
            Assert.That(placement.HintText, Does.Contain("Full size"));
        }
    }
}
