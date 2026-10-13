using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fieldmate.Localization;
using Fieldmate.Vision;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Procedures;

/// <summary>
/// #24 guard: a button's label is an English key that <see cref="Fieldmate.Procedures.PressButton"/> shows in the current
/// language, so every label it can get (SetLabel literals, the scene builder's, the computed ones) needs a German entry.
/// </summary>
public class ButtonLabelTests
{
    private static readonly Regex SetLabel = new(@"SetLabel\(\s*(?:[^;]*?\?\s*)?""((?:[^""\\]|\\.)*)""(?:\s*:\s*""((?:[^""\\]|\\.)*)"")?",
        RegexOptions.Compiled);

    private static readonly Regex BuilderLabel = new(@"Set\([^,]+,\s*""label"",\s*""((?:[^""\\]|\\.)*)""\)", RegexOptions.Compiled);

    [Test]
    public void Every_button_label_has_a_German_entry()
    {
        var labels = (from file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories)
                      where !file.Contains("Fieldmate.Tests")
                      let source = File.ReadAllText(file)
                      from match in SetLabel.Matches(source).Cast<Match>().Concat(BuilderLabel.Matches(source).Cast<Match>())
                      from g in match.Groups.Cast<Group>().Skip(1)
                      where g.Success
                      select g.Value).ToList();
        labels.AddRange(Enum.GetValues(typeof(OcclusionMode)).Cast<OcclusionMode>().Select(OcclusionPlan.Label));
        labels.Add(PrivacySettings.ButtonLabel(true));
        labels.Add(PrivacySettings.ButtonLabel(false));
        labels.Add(HandMenu.LanguageLabel);

        Assert.That(labels, Does.Contain("Confirm restart").And.Contain("Labels: Off").And.Contain("Fit controllers"), "the scan finds them");
        Assert.That(labels.Distinct().Where(l => !GermanText.Table.ContainsKey(l)), Is.Empty, "add these to GermanText.Table");
    }

    [Test]
    public void The_language_row_names_the_language_it_is_in()
    {
        Assert.That(HandMenu.LanguageLabel, Is.EqualTo("Language: English"));
        Assert.That(GermanText.Table[HandMenu.LanguageLabel], Is.EqualTo("Sprache: Deutsch"));
    }
}
