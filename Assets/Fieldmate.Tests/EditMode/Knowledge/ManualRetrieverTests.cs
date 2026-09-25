using System;
using System.Linq;
using Fieldmate.Knowledge;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Knowledge;

public class ManualRetrieverTests
{
    private MachineManual manual;
    private ManualRetriever retriever;

    [SetUp]
    public void SetUp()
    {
        manual = MachineManual.Parse(MachineManualTests.Sample);
        retriever = new ManualRetriever(manual);
    }

    private string[] Ids(RetrievalQuery query) => retriever.Retrieve(query).Sections.Select(s => s.Id).ToArray();

    [Test]
    public void GazePart_SelectsItsSections_WithSafetyFirst()
    {
        Assert.That(Ids(new RetrievalQuery(gazePartId: "valve")), Is.EqualTo(new[] { "safety.lock", "part.valve" }));
    }

    [Test]
    public void Step_SelectsItsSections()
    {
        Assert.That(Ids(new RetrievalQuery(stepId: "lock")), Is.EqualTo(new[] { "safety.lock" }));
    }

    [Test]
    public void Keywords_MatchTitlesKeywordsAndText()
    {
        Assert.That(Ids(new RetrievalQuery(text: "Why is there water everywhere?")).First(), Is.EqualTo("fault.leak"));
        Assert.That(Ids(new RetrievalQuery(text: "where is the padlock")), Is.EqualTo(new[] { "safety.lock" }));
        Assert.That(Ids(new RetrievalQuery(text: "flow")), Is.EqualTo(new[] { "part.valve" }), "matched in body text");
    }

    [Test]
    public void EmptyOrUnrelatedQuery_ReturnsNothing()
    {
        Assert.That(Ids(default), Is.Empty);
        Assert.That(Ids(new RetrievalQuery(text: "the and what")), Is.Empty, "stop words only");
        Assert.That(Ids(new RetrievalQuery(gazePartId: "unknown", text: "banana")), Is.Empty);
    }

    [Test]
    public void Slice_IsBoundedBySectionCountAndCharacters()
    {
        var one = new ManualRetriever(manual, maxSections: 1).Retrieve(new RetrievalQuery(gazePartId: "valve"));
        Assert.That(one.Sections, Has.Count.EqualTo(1));
        Assert.That(one.Truncated, Is.True);

        var tiny = new ManualRetriever(manual, maxCharacters: 30).Retrieve(new RetrievalQuery(gazePartId: "valve"));
        Assert.That(tiny.Sections.Select(s => s.Id), Is.EqualTo(new[] { "part.valve" }), "skips the longer section, keeps what fits");
        Assert.That(tiny.Characters, Is.LessThanOrEqualTo(30));
        Assert.That(tiny.Truncated, Is.True);

        var all = retriever.Retrieve(new RetrievalQuery(gazePartId: "valve"));
        Assert.That(all.Truncated, Is.False);
        Assert.That(all.Characters, Is.EqualTo(all.Sections.Sum(s => s.Text.Length)));
    }

    [Test]
    public void PromptText_CitesSectionIds()
    {
        var text = retriever.Retrieve(new RetrievalQuery(gazePartId: "valve")).ToPromptText();

        Assert.That(text, Does.StartWith("[safety.lock] Lockout\n"));
        Assert.That(text, Does.Contain("[part.valve] Valve\nThe valve controls flow."));
    }

    [Test]
    public void Score_ExplainsTheWeights()
    {
        var safety = manual.Sections.First(s => s.Id == "safety.lock");

        var expected = ManualRetriever.GazeWeight + ManualRetriever.StepWeight + ManualRetriever.SafetyBoost + ManualRetriever.KeywordWeight;
        Assert.That(retriever.Score(safety, new RetrievalQuery("valve", "lock", "padlock")), Is.EqualTo(expected));
        Assert.Throws<ArgumentException>(() => retriever.Score(new ManualSection("x", SectionKind.Part, "x", "x",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()), default));
    }

    [Test]
    public void Tokenize_LowercasesDropsStopWordsAndPlurals()
    {
        Assert.That(ManualRetriever.Tokenize("Why are the VALVES leaking, and 3 bolts?"),
            Is.EquivalentTo(new[] { "valve", "leaking", "bolt" }));
        Assert.That(ManualRetriever.Tokenize("pressure glass"), Is.EquivalentTo(new[] { "pressure", "glass" }), "no 'ss' stripping");
        Assert.That(ManualRetriever.Tokenize(null), Is.Empty);
    }

    [Test]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ManualRetriever(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualRetriever(manual, maxSections: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualRetriever(manual, maxCharacters: 0));
    }
}
