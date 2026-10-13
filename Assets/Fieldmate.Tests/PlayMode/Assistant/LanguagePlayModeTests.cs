using System.Collections;
using System.Linq;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Localization;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#24: German mode switches live: the mate's bubble, the step card, the labels and the hand menu re-render.</summary>
public class LanguagePlayModeTests
{
    private AssistantPanel panel;
    private ProcedurePanel card;
    private HandMenu menu;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        Lang.Set(Lang.English);
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        panel = Object.FindAnyObjectByType<AssistantPanel>();
        card = Object.FindAnyObjectByType<ProcedurePanel>();
        menu = Object.FindAnyObjectByType<HandMenu>();
    }

    [TearDown]
    public void BackToEnglish()
    {
        Lang.Set(Lang.English); // other tests expect English
        ControlTag.ShowAll = false;
    }

    private static PressButton Row(string name) =>
        Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == name);

    private static string German(string english)
    {
        Assert.That(GermanText.Table.ContainsKey(english), Is.True, $"no German for \"{english}\"");
        return GermanText.Table[english];
    }

    [UnityTest]
    public IEnumerator Switching_to_German_rerenders_the_bubble_card_and_labels_without_a_reload()
    {
        var idle = panel.StateLabel;
        var title = card.TitleText;
        var breaker = Object.FindObjectsByType<ControlTag>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(t => t.Text.StartsWith("Main breaker"));
        var machine = Object.FindAnyObjectByType<MachineServices>();
        panel.ShowStep(machine.Runner.Definition, 2);
        Assert.That(panel.DetailText, Does.Contain("Step 2 of"));

        Lang.Set(Lang.German);
        yield return null;
        Assert.That(panel.StateLabel, Is.EqualTo(German(idle)), "the idle line");
        Assert.That(card.TitleText, Is.EqualTo(German(title)), "the step card");
        Assert.That(breaker.Text, Does.StartWith("Hauptschalter"));
        Assert.That(panel.DetailText, Does.Contain("Schritt 2 von").And.Contain(German(machine.Runner.Definition.Steps[1].Title)));
        Assert.That(Row("Labels Button").Label, Is.EqualTo("Beschriftungen: Aus"));
        Assert.That(Row("Language Button").Label, Is.EqualTo("Sprache: Deutsch"));

        Lang.Set(Lang.English);
        yield return null;
        Assert.That(panel.StateLabel, Is.EqualTo(idle));
        Assert.That(card.TitleText, Is.EqualTo(title));
        Assert.That(breaker.Text, Does.StartWith("Main breaker"));
        Assert.That(panel.DetailText, Does.Contain("Step 2 of"));
        Assert.That(Row("Labels Button").Label, Is.EqualTo("Labels: Off"));
    }

    [UnityTest]
    public IEnumerator A_busy_state_line_follows_the_language()
    {
        var go = new GameObject("Panel under test", typeof(RectTransform), typeof(AssistantPanel));
        var own = go.GetComponent<AssistantPanel>();
        own.SetState(AssistantState.Listening, "idle");
        Assert.That(own.StateLabel, Is.EqualTo("Listening…"));

        Lang.Set(Lang.German);
        yield return null;
        Assert.That(own.StateLabel, Is.EqualTo("Ich höre zu…"));
        own.SetState(AssistantState.Thinking, "idle");
        Assert.That(own.StateLabel, Is.EqualTo("Ich denke nach…"));
        Object.Destroy(go);
        yield return null;
        Lang.Set(Lang.English); // a destroyed panel must have unsubscribed (no MissingReferenceException)
    }

    [UnityTest]
    public IEnumerator The_hand_menu_language_row_toggles_German_and_back()
    {
        menu.Toggle();
        yield return null;
        var language = Row("Language Button");
        Assert.That(language.transform.IsChildOf(menu.transform), Is.True);
        Assert.That(language.Label, Is.EqualTo("Language: English"));

        language.Press();
        yield return null;
        Assert.That(Lang.IsGerman, Is.True);
        Assert.That(language.Label, Is.EqualTo("Sprache: Deutsch"));

        yield return new WaitForSecondsRealtime(0.7f); // past the press debounce
        language.Press();
        yield return null;
        Assert.That(Lang.Current, Is.EqualTo(Lang.English));
        Assert.That(language.Label, Is.EqualTo("Language: English"));
    }
}
