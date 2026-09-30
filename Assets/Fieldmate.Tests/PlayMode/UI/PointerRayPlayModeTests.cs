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
    }
}
