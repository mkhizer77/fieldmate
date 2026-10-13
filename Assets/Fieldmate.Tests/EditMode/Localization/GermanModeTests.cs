using System.IO;
using System.Linq;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Localization;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Localization;

/// <summary>#24: German mode switches what the user sees and hears; English stays exactly as it was.</summary>
public class GermanModeTests
{
    private MachineManual manual;
    private PartCatalog catalog;
    private ProcedureDefinition procedure;

    [SetUp]
    public void SetUp()
    {
        Lang.ResetForTests();
        manual = MachineManual.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Manual", "manual.json")));
        catalog = PartCatalog.FromManual(manual);
        procedure = DemoProcedures.ReliefValveReplacement();
    }

    [TearDown]
    public void TearDown() => Lang.ResetForTests();

    private StepDefinition Step(string id) => procedure.Steps.First(s => s.Id == id);

    [Test]
    public void Set_normalises_and_notifies_only_on_change()
    {
        var heard = 0;
        void OnChanged(string _) => heard++;
        Lang.Changed += OnChanged;
        try
        {
            Assert.That(Lang.Set("de-DE"), Is.True);
            Assert.That(Lang.Current, Is.EqualTo(Lang.German));
            Assert.That(Lang.Set("de"), Is.False, "no change, no event");
            Assert.That(Lang.Set("fr"), Is.True, "unknown means English");
            Assert.That(Lang.Current, Is.EqualTo(Lang.English));
            Assert.That(heard, Is.EqualTo(2));
        }
        finally
        {
            Lang.Changed -= OnChanged;
        }
    }

    [Test]
    public void T_translates_only_while_German_is_on_and_falls_back_to_English()
    {
        Assert.That(Lang.T("Lock out the main breaker"), Is.EqualTo("Lock out the main breaker"));
        Lang.Set(Lang.German);
        Assert.That(Lang.T("Lock out the main breaker"), Is.EqualTo("Hauptschalter abschließen"));
        Assert.That(Lang.T("no German for this"), Is.EqualTo("no German for this"));
    }

    [Test]
    public void Every_part_and_section_has_German_with_a_valid_gender()
    {
        foreach (var part in manual.Parts)
        {
            Assert.That(part.German, Is.Not.Null, part.Id);
            Assert.That(part.German.Name, Is.Not.Empty, part.Id);
            Assert.That(new[] { "m", "f", "n", "pl" }, Does.Contain(part.German.Gender), part.Id);
        }

        Assert.That(manual.Sections.Where(s => s.GermanTitle == null || s.GermanText == null).Select(s => s.Id), Is.Empty);
        Assert.That(manual.Faults.Where(f => f.GermanName == null).Select(f => f.Id), Is.Empty);
    }

    [Test]
    public void Every_step_title_and_safety_rule_has_German()
    {
        var missing = procedure.Steps.Select(s => s.Title).Concat(procedure.SafetyRules.Select(r => r.Description)).Append(procedure.Title)
            .Where(english => !GermanText.Table.ContainsKey(english));
        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void Part_names_follow_the_language_with_articles_in_German()
    {
        Assert.That(catalog.DisplayName("main_breaker"), Is.EqualTo("Main breaker"));
        Lang.Set(Lang.German);
        Assert.That(catalog.DisplayName("main_breaker"), Is.EqualTo("Hauptschalter"));
        Assert.That(catalog.WithArticle("main_breaker", GermanCase.Accusative), Is.EqualTo("den Hauptschalter"));
        Assert.That(catalog.WithArticle("inlet_valve", GermanCase.Accusative), Is.EqualTo("das Einlassventil"));
        Assert.That(catalog.WithArticle("pressure_line", GermanCase.Nominative), Is.EqualTo("die Druckleitung"));
        Assert.That(manual.TryGetSection("safety.loto", out var loto) && loto.TextIn(Lang.Current).StartsWith("Vor jeder Wartung"));
    }

    [Test]
    public void Step_instructions_in_German_use_the_part_gender()
    {
        Lang.Set(Lang.German);

        Assert.That(StepInstructions.For(Step("inspect"), catalog), Is.EqualTo("Schau dir das Überdruckventil einen Moment an."));
        Assert.That(StepInstructions.For(Step("lockout"), catalog), Does.Contain("beiden Händen").And.Contain("GESPERRT"));
        Assert.That(StepInstructions.For(Step("lockout"), catalog, Modality.Controllers), Does.Contain("beiden Controllern"));
        Assert.That(StepInstructions.Spoken(Step("close_inlet"), catalog), Is.EqualTo("Greif das Einlassventil und dreh es zu."));
        Assert.That(StepInstructions.Spoken(Step("remove_cover"), catalog), Is.EqualTo("Greif den Pumpendeckel und zieh ihn zu dir hin ab."));
        Assert.That(StepInstructions.For(Step("replace"), catalog), Is.EqualTo("Greif den Ventileinsatz auf dem Tablett und drück ihn in den Ventilsitz."));
        Assert.That(StepInstructions.For(Step("verify_zero"), catalog), Does.Contain("0 ± 0,2 bar"), "decimal comma");
        Assert.That(StepInstructions.Short(Step("open_inlet")), Is.EqualTo("greifen · auf OFFEN drehen"));
    }

    [Test]
    public void English_step_instructions_are_unchanged()
    {
        Assert.That(StepInstructions.For(Step("inspect"), catalog), Is.EqualTo("Look at the pressure relief valve for a moment."));
        Assert.That(StepInstructions.Spoken(Step("close_inlet"), catalog), Is.EqualTo("Pinch the inlet valve and turn it to closed."));
    }

    [Test]
    public void Narration_in_German_speaks_the_step_and_the_rule()
    {
        Lang.Set(Lang.German);

        var line = Narration.StepDone(1, procedure.Steps.Count, Step("lockout"), "Greif den roten Griff.");
        Assert.That(line, Is.EqualTo("Gut gemacht. Jetzt: Hauptschalter abschließen. Greif den roten Griff."));
        Assert.That(Narration.StepDone(9, 10, Step("verify_running"), null), Does.Contain("Letzter Schritt: Prüfen, ob der Betriebsdruck"));
        Assert.That(Narration.StepDone(10, 10, null, null), Does.EndWith("Das war der letzte Schritt."));
        Assert.That(Narration.Violation(procedure.SafetyRules.First(r => r.Id == "loto_inlet")),
            Is.EqualTo("Moment. Erst den Hauptschalter abschließen, dann das Einlassventil anfassen."));
        Assert.That(Narration.Undone("der Hauptschalter", "locked", null), Is.EqualTo("Vorsicht, der Hauptschalter ist nicht mehr gesperrt."));
    }

    [Test]
    public void Setup_lines_in_German()
    {
        Lang.Set(Lang.German);

        Assert.That(SetupScript.RoomScan, Does.StartWith("Ich schaue mir deinen Raum an"));
        Assert.That(SetupScript.Intro(InputWords.PlaceMate(Modality.Hands)), Does.StartWith("Hallo, ich bin Fieldmate").And.Contain("Daumen und Zeigefinger"));
        Assert.That(SetupScript.Briefing(procedure, null),
            Does.StartWith("Heute steht an: Ventileinsatz des Überdruckventils tauschen. Das sind zehn Schritte.").And.EndWith("Zuerst: Überdruckventil prüfen."));
    }

    [Test]
    public void Offline_answers_understand_and_answer_German()
    {
        Lang.Set(Lang.German);
        var runner = new ProcedureRunner(procedure);
        var fallback = new FallbackResponses(manual, runner, new TelemetryModel(FaultModel.CreateDefault()));

        var where = fallback.Answer("Wo ist das Manometer?");
        Assert.That(where.Intent, Is.EqualTo("where"));
        Assert.That(where.HighlightPartId, Is.EqualTo("pressure_gauge"));
        Assert.That(where.Text, Does.StartWith("Manometer:").And.EndWith("Ich habe es markiert."));

        Assert.That(fallback.Answer("Was soll ich jetzt machen?").Text, Does.Contain("Drück Start"));
        Assert.That(fallback.Answer("Wie hoch ist der Druck?").Text, Does.StartWith("Gerade: Druck"));

        runner.Restart(1);
        Assert.That(fallback.Answer("Und weiter?").Text, Does.StartWith("Jetzt: Überdruckventil prüfen."));
    }

    [Test]
    public void The_German_system_prompt_lists_the_German_part_names()
    {
        var german = AssistantPrompt.System(manual.MachineName, "de", vision: false, manual.Parts);
        Assert.That(german, Does.Contain("Reply in German.").And.Contain("main_breaker = Hauptschalter"));
        Assert.That(AssistantPrompt.System(manual.MachineName, "en", vision: false, manual.Parts), Does.Not.Contain("Hauptschalter"));
    }
}
