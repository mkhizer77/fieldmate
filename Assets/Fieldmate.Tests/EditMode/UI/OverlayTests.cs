using Fieldmate.UI;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.UI;

/// <summary>Device test 2026-09-30: UI, tags and markers cut into the machine; everything in the UI layer draws on top.</summary>
public class OverlayTests
{
    [Test]
    public void Overlay_shaders_exist_and_the_kit_uses_them()
    {
        Assert.That(Shader.Find(UiKit.ImageOverlayShader), Is.Not.Null);
        Assert.That(Shader.Find("Fieldmate/UnlitOverlay"), Is.Not.Null);
        Assert.That(Shader.Find(UiKit.TextOverlayShader), Is.Not.Null, "TMP essentials ship the mobile overlay shader");
        Assert.That(UiKit.ImageOverlay.shader.name, Is.EqualTo(UiKit.ImageOverlayShader));
        var text = UiKit.TextOverlay(Theme.SemiBold);
        Assert.That(text.shader.name, Is.EqualTo(UiKit.TextOverlayShader));
        Assert.That(text.mainTexture, Is.SameAs(Theme.SemiBold.material.mainTexture), "same SDF atlas");
        Assert.That(UiKit.TextOverlay(Theme.SemiBold), Is.SameAs(text), "one material per font");
    }

    [Test]
    public void Guide_material_draws_on_top()
    {
        var guide = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Placeholders/Materials/PlacementGuide.mat");
        Assert.That(guide, Is.Not.Null);
        Assert.That(guide.shader.name, Is.EqualTo("Fieldmate/UnlitOverlay"));
    }
}
