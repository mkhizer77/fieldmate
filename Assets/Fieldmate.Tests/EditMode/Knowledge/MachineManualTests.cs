using System.Linq;
using Fieldmate.Json;
using Fieldmate.Knowledge;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Knowledge;

public class MachineManualTests
{
    internal const string Sample = @"{
  ""machine"": ""Test skid"",
  ""parts"": [
    { ""id"": ""valve"", ""name"": ""Valve"", ""description"": ""A valve."", ""safety"": ""Mind the pressure."" },
    { ""id"": ""breaker"", ""name"": ""Breaker"" }
  ],
  ""faults"": [ { ""id"": ""leak"", ""name"": ""Leak"", ""symptoms"": ""Water on floor"", ""cause"": ""Seal"", ""procedure"": ""fix_leak"" } ],
  ""procedures"": [ { ""id"": ""fix_leak"", ""title"": ""Fix the leak"", ""steps"": [
    { ""id"": ""lock"", ""title"": ""Lock out"", ""part"": ""breaker"" },
    { ""id"": ""done"", ""title"": ""Done"" } ] } ],
  ""sections"": [
    { ""id"": ""safety.lock"", ""kind"": ""safety"", ""title"": ""Lockout"", ""text"": ""Lock the breaker before touching the valve."",
      ""parts"": [""breaker"", ""valve""], ""steps"": [""lock""], ""keywords"": [""lockout"", ""padlock""] },
    { ""id"": ""part.valve"", ""kind"": ""part"", ""title"": ""Valve"", ""text"": ""The valve controls flow."", ""parts"": [""valve""] },
    { ""id"": ""fault.leak"", ""kind"": ""fault"", ""title"": ""Leak"", ""text"": ""Water on the floor means a leaking seal."", ""keywords"": [""leak"", ""water""] }
  ]
}";

    [Test]
    public void Parse_ReadsAllCollections()
    {
        var manual = MachineManual.Parse(Sample);

        Assert.That(manual.MachineName, Is.EqualTo("Test skid"));
        Assert.That(manual.Parts.Select(p => p.Id), Is.EqualTo(new[] { "valve", "breaker" }));
        Assert.That(manual.Parts[1].Description, Is.Empty, "optional fields default to empty");
        Assert.That(manual.Faults.Single().ProcedureId, Is.EqualTo("fix_leak"));
        Assert.That(manual.Faults.Single().Symptoms, Is.EqualTo("Water on floor"));
        Assert.That(manual.Procedures.Single().Steps.Select(s => s.Id), Is.EqualTo(new[] { "lock", "done" }));
        Assert.That(manual.Procedures.Single().Steps[1].PartId, Is.Null);
        Assert.That(manual.Sections[0].Kind, Is.EqualTo(SectionKind.Safety));
        Assert.That(manual.Sections[0].Keywords, Is.EqualTo(new[] { "lockout", "padlock" }));
        Assert.That(manual.Validate(), Is.Empty);
    }

    [Test]
    public void Lookups_FindPartsAndSections()
    {
        var manual = MachineManual.Parse(Sample);

        Assert.That(manual.TryGetPart("valve", out var valve) && valve.Safety == "Mind the pressure.", Is.True);
        Assert.That(manual.TryGetPart("nope", out _), Is.False);
        Assert.That(manual.TryGetPart(null, out _), Is.False);
        Assert.That(manual.TryGetSection("fault.leak", out var leak) && leak.Title == "Leak", Is.True);
        Assert.That(manual.TryGetSection("nope", out _), Is.False);
    }

    [TestCase("[]", "must be a JSON object")]
    [TestCase("{\"parts\": {}}", "'parts' must be an array")]
    [TestCase("{\"parts\": [1]}", "'parts[0]' must be an object")]
    [TestCase("{\"parts\": [{\"name\": \"x\"}]}", "'parts[0].id' is required")]
    [TestCase("{\"sections\": [{\"id\": \"s\", \"kind\": \"poem\", \"title\": \"t\", \"text\": \"x\"}]}", "'sections[0].kind'")]
    [TestCase("{\"procedures\": [{\"id\": \"p\", \"title\": \"t\", \"steps\": [{\"id\": \"s\"}]}]}", "'procedures[0].steps[0].title' is required")]
    public void Parse_ReportsStructuralProblemsWithPath(string json, string expected)
    {
        var e = Assert.Throws<ManualFormatException>(() => MachineManual.Parse(json));

        Assert.That(e.Message, Does.Contain(expected));
    }

    [Test]
    public void Parse_PropagatesJsonSyntaxErrors()
    {
        Assert.Throws<JsonException>(() => MachineManual.Parse("{ parts: [] }"));
    }

    [Test]
    public void Parse_EmptyObject_IsAnEmptyManual()
    {
        var manual = MachineManual.Parse("{}");

        Assert.That(manual.Parts, Is.Empty);
        Assert.That(manual.Sections, Is.Empty);
        Assert.That(manual.MachineName, Is.Empty);
    }

    [Test]
    public void Validate_FindsDuplicatesAndBrokenReferences()
    {
        const string json = @"{
  ""parts"": [ { ""id"": ""a"", ""name"": ""A"" }, { ""id"": ""a"", ""name"": ""A2"" } ],
  ""faults"": [ { ""id"": ""f"", ""name"": ""F"", ""procedure"": ""missing_proc"" } ],
  ""procedures"": [ { ""id"": ""p"", ""title"": ""P"", ""steps"": [ { ""id"": ""s"", ""title"": ""S"", ""part"": ""ghost"" }, { ""id"": ""s"", ""title"": ""S2"" } ] } ],
  ""sections"": [ { ""id"": ""x"", ""title"": ""X"", ""text"": ""t"", ""parts"": [""ghost""], ""steps"": [""nostep""] },
                  { ""id"": ""x"", ""title"": ""X2"", ""text"": ""t"" } ]
}";

        var problems = MachineManual.Parse(json).Validate();

        Assert.That(problems, Has.Some.Contains("Duplicate part id 'a'"));
        Assert.That(problems, Has.Some.Contains("Duplicate section id 'x'"));
        Assert.That(problems, Has.Some.Contains("Duplicate step id 's'"));
        Assert.That(problems, Has.Some.Contains("Step 's' references unknown part 'ghost'"));
        Assert.That(problems, Has.Some.Contains("Fault 'f' references unknown procedure 'missing_proc'"));
        Assert.That(problems, Has.Some.Contains("Section 'x' references unknown part 'ghost'"));
        Assert.That(problems, Has.Some.Contains("Section 'x' references unknown step 'nostep'"));
    }

    [Test]
    public void FindPartsMissingFrom_ListsUntaggedManualParts()
    {
        var manual = MachineManual.Parse(Sample);

        Assert.That(manual.FindPartsMissingFrom(new[] { "valve", "extra" }), Is.EqualTo(new[] { "breaker" }));
        Assert.That(manual.FindPartsMissingFrom(null), Is.EqualTo(new[] { "valve", "breaker" }));
    }
}
