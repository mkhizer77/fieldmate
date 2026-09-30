using System.Collections.Generic;
using Fieldmate.Interaction;
using Fieldmate.Procedures;
using Fieldmate.UI;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.UI;

/// <summary>#53: the design system's pure parts — state colours, tag markup, the debrief text, the shared sprite and fonts.</summary>
public class UiKitTests
{
    [TestCase("on", StateMeaning.Live)]
    [TestCase("OPEN", StateMeaning.Live)]
    [TestCase("running", StateMeaning.Live)]
    [TestCase("off", StateMeaning.Passive)]
    [TestCase("closed", StateMeaning.Passive)]
    [TestCase("removed", StateMeaning.Passive)]
    [TestCase("locked", StateMeaning.Safe)]
    [TestCase("tagged", StateMeaning.Safe)]
    [TestCase("", StateMeaning.None)]
    [TestCase(null, StateMeaning.None)]
    [TestCase("banana", StateMeaning.None)]
    public void State_meaning_is_machine_neutral(string state, StateMeaning expected) =>
        Assert.That(TagStateStyle.Meaning(state), Is.EqualTo(expected));

    [Test]
    public void Live_states_warn_and_secured_states_reassure()
    {
        Assert.That(TagStateStyle.Color("on"), Is.EqualTo(Theme.Warning));
        Assert.That(TagStateStyle.Color("locked"), Is.EqualTo(Theme.Success));
        Assert.That(TagStateStyle.Color("closed"), Is.EqualTo(Theme.Neutral));
    }

    [Test]
    public void Rich_tag_text_keeps_the_plain_words_and_colours_the_state()
    {
        var rich = ControlTagText.Rich("Main breaker", "pump motor power", "locked");
        Assert.That(rich, Does.StartWith("Main breaker\n<size=26>"));
        Assert.That(rich, Does.Contain($"<color={Theme.SuccessHex}>LOCKED</color>"));
        Assert.That(rich, Does.Contain("pump motor power · "));
        Assert.That(ControlTagText.Rich("Gauge", null, null), Is.EqualTo("Gauge"));
        Assert.That(ControlTagText.Rich("Cover", null, "removed"), Does.Contain("REMOVED"));
    }

    [Test]
    public void Debrief_text_shows_score_time_and_at_most_four_notes()
    {
        var errors = new List<ProcedureError>();
        for (var i = 0; i < 6; i++)
        {
            errors.Add(new ProcedureError(i, "step", $"error {i}"));
        }

        var violations = new List<SafetyRule> { new("loto", "Lock out first", "cover", "breaker", "locked") };
        var result = new ProcedureResult("p", 125, new double[0], errors, violations, 2, 71, false);
        var text = ProcedurePanel.DebriefText(result);
        Assert.That(text, Does.Contain(">71</color>"));
        Assert.That(text, Does.Contain("Time 2:05"));
        Assert.That(text, Does.Contain("Errors 6"));
        Assert.That(text, Does.Contain("Lock out first"), "violations come first");
        Assert.That(text, Does.Contain("error 2"));
        Assert.That(text, Does.Not.Contain("error 3"), "four notes at most");
    }

    [Test]
    public void Rounded_sprite_is_sliced_and_shared()
    {
        var sprite = UiKit.Rounded;
        Assert.That(sprite.border.x, Is.GreaterThan(0f));
        Assert.That(sprite.texture.width, Is.EqualTo(96));
        Assert.That(UiKit.Rounded, Is.SameAs(sprite));
    }

    [Test]
    public void Inter_font_assets_are_committed()
    {
        Assert.That(Theme.Regular, Is.Not.Null);
        Assert.That(Theme.Regular.name, Is.EqualTo("InterRegular SDF"), "run Fieldmate → Build UI Fonts and commit Assets/_Project/Resources/Fonts");
        Assert.That(Theme.SemiBold.name, Is.EqualTo("InterSemiBold SDF"));
        Assert.That(Theme.SemiBold.HasCharacter('ß'), "German text must not fall back");
        Assert.That(Theme.SemiBold.HasCharacter('✓'));
    }

    [Test]
    public void No_machine_name_is_hard_coded_in_the_ui_layer()
    {
        // The concept is moving beyond the pump skid: every name on screen comes from manual/procedure data.
        foreach (var file in System.IO.Directory.GetFiles("Assets/Fieldmate.Unity", "*.cs", System.IO.SearchOption.AllDirectories))
        {
            var text = System.IO.File.ReadAllText(file);
            Assert.That(text, Does.Not.Contain("FM-200"), file);
        }
    }
}
