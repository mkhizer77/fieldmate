using System.IO;
using System.Linq;
using Fieldmate.Knowledge;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Knowledge;

/// <summary>The shipped manual.json: consistent, and retrieval grounds the demo's scripted questions.</summary>
public class ManualContentTests
{
    private MachineManual manual;
    private ManualRetriever retriever;

    [OneTimeSetUp]
    public void Load()
    {
        manual = MachineManual.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Manual", "manual.json")));
        retriever = new ManualRetriever(manual);
    }

    [Test]
    public void Manual_IsConsistent()
    {
        Assert.That(manual.Validate(), Is.Empty);
        Assert.That(manual.Parts, Has.Count.GreaterThanOrEqualTo(10));
        Assert.That(manual.Sections.All(s => s.Text.Length <= 500), Is.True, "sections stay short enough to combine");
    }

    [Test]
    public void FaultIds_MatchTheTwinFaults()
    {
        Assert.That(manual.Faults.Select(f => f.Id), Is.EquivalentTo(new[] { "overpressure", "overheating", "loose_mount" }));
    }

    [TestCase("Why is the pressure high?", null, null, "fault.overpressure")]
    [TestCase("What does the red alarm on the status board mean?", null, null, "overview.status_board")]
    [TestCase("Why is it shaking so much?", null, null, "fault.loose_mount")]
    [TestCase("What is this?", "relief_valve", null, "part.relief_valve")]
    [TestCase("How do I do this step?", null, "lockout", "safety.loto")]
    [TestCase("What should the gauge read now?", null, "verify_zero", "safety.stored_pressure")]
    public void ScriptedQuestions_RetrieveTheExpectedSection(string question, string gaze, string step, string expected)
    {
        var slice = retriever.Retrieve(new RetrievalQuery(gaze, step, question));

        Assert.That(slice.Sections.Select(s => s.Id), Does.Contain(expected));
        Assert.That(slice.Characters, Is.LessThanOrEqualTo(retriever.MaxCharacters));
    }
}
