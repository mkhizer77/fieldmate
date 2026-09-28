using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>Scripted hands for PlayMode tests: direct interactors with their select button held, grabbed via XRI.</summary>
internal sealed class ScriptedHands
{
    private readonly XRInteractionManager manager;

    public ScriptedHands(XRInteractionManager manager) => this.manager = manager;

    public XRDirectInteractor Hand(Vector3 position)
    {
        var go = new GameObject("Test Hand", typeof(SphereCollider), typeof(Rigidbody));
        go.GetComponent<SphereCollider>().isTrigger = true;
        go.GetComponent<SphereCollider>().radius = 0.01f;
        go.GetComponent<Rigidbody>().isKinematic = true;
        go.transform.position = position;
        var hand = go.AddComponent<XRDirectInteractor>();
        hand.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
        hand.selectInput.manualPerformed = true;
        hand.selectInput.manualValue = 1f;
        return hand;
    }

    public void Grab(XRBaseInteractor hand, IXRSelectInteractable target) => manager.SelectEnter((IXRSelectInteractor)hand, target);

    public void Release(XRBaseInteractor hand, IXRSelectInteractable target) => manager.SelectExit((IXRSelectInteractor)hand, target);

    public void Seat(IXRSelectInteractor socket, IXRSelectInteractable tool) => manager.SelectEnter(socket, tool);

    /// <summary>A point on a circle about a pivot's axis (both in the pivot's parent space), in world space.</summary>
    public static Vector3 AroundAxis(Transform pivot, Vector3 localAxis, Vector3 localRadius, float degrees) =>
        pivot.parent.TransformPoint(pivot.localPosition + Quaternion.AngleAxis(degrees, localAxis) * localRadius);
}
