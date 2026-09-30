using System.Collections;
using System.Linq;
using Fieldmate.Procedures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Tests.PlayMode.UI;

/// <summary>#58: pointer rays press the machine's buttons from a distance and nothing else.</summary>
public class PointerRayPlayModeTests
{
    [UnityTest]
    public IEnumerator Rays_see_only_the_buttons_and_buttons_accept_both()
    {
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/AssistantBench.unity", LoadSceneMode.Single);
        yield return null;
        var buttons = InteractionLayerMask.GetMask("Buttons");
        Assert.That((int)buttons, Is.Not.EqualTo(0), "the Buttons interaction layer is named");

        var rays = Object.FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None);
        Assert.That(rays.Select(r => r.name), Is.EquivalentTo(new[] { "Left Pointer", "Right Pointer" }));
        foreach (var ray in rays)
        {
            Assert.That((int)ray.interactionLayers, Is.EqualTo((int)buttons), $"{ray.name} only sees buttons");
            Assert.That(ray.enableUIInteraction, Is.False);
        }

        foreach (var button in Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None))
        {
            Assert.That((int)button.interactionLayers & (int)buttons, Is.Not.EqualTo(0), $"{button.name} on the Buttons layer");
            Assert.That((int)button.interactionLayers & (int)InteractionLayerMask.GetMask("Default"), Is.Not.EqualTo(0), $"{button.name} still pressed by hand");
        }

        foreach (var control in Object.FindObjectsByType<XRBaseInteractable>(FindObjectsSortMode.None).Where(i => i is not PressButton))
        {
            Assert.That((int)control.interactionLayers & (int)buttons, Is.EqualTo(0), $"{control.name}: machine controls are hands-only");
        }

        // White at rest with a dot at the end, blue while pressing, white again on release.
        foreach (var ray in rays)
        {
            var style = ray.GetComponent<Fieldmate.XR.PointerRayStyle>();
            Assert.That(style, Is.Not.Null, $"{ray.name} has a style");
            Assert.That(style.IsPressing, Is.False);
            Assert.That(style.CurrentColor, Is.EqualTo(Fieldmate.XR.PointerRayStyle.Idle));
            Assert.That(ray.transform.Find("Ray Dot"), Is.Not.Null, "end dot");
            Assert.That(ray.transform.Find("Ray Dot").GetComponent<Collider>(), Is.Null, "the dot never blocks a ray or the gaze");
            Assert.That(ray.GetComponent<LineRenderer>().sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.white), "no tint: the gradient sets white / blue");
            style.Apply(true);
            Assert.That(style.CurrentColor, Is.EqualTo(Fieldmate.XR.PointerRayStyle.Pressing));
            Assert.That(ray.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>().validColorGradient.Evaluate(0.5f).b,
                Is.GreaterThan(0.9f), "blue line while pressing");
            style.Apply(false);
            Assert.That(style.CurrentColor, Is.EqualTo(Fieldmate.XR.PointerRayStyle.Idle));
        }
    }
}
