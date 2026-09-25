using System;
using Fieldmate.Procedures;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Procedures;

public class ProcedureDefinitionTests
{
    [Test]
    public void Fixture_IsValid()
    {
        Assert.That(ReliefValveProcedure.Create().Validate(), Is.Empty);
    }

    [Test]
    public void Validate_ReportsEmptyDuplicateAndMissingEntries()
    {
        var step = StepDefinition.Confirm("a", "A");
        var rule = new SafetyRule("r", "R", "x", "y", "z");

        Assert.That(new ProcedureDefinition("p", "P", Array.Empty<StepDefinition>()).Validate(), Has.Some.Contains("no steps"));
        Assert.That(new ProcedureDefinition("p", "P", new[] { step, step }).Validate(), Has.Some.Contains("Duplicate step id 'a'"));
        Assert.That(new ProcedureDefinition("p", "P", new[] { step, null }).Validate(), Has.Some.Contains("Step 2 is missing"));
        Assert.That(new ProcedureDefinition("p", "P", new[] { step }, new[] { rule, rule }).Validate(), Has.Some.Contains("Duplicate safety rule"));
        Assert.That(new ProcedureDefinition("p", "P", new[] { step }, new SafetyRule[] { null }).Validate(), Has.Some.Contains("safety rule is missing"));
    }

    [Test]
    public void Constructor_ValidatesArgumentsAndDefaults()
    {
        Assert.Throws<ArgumentException>(() => new ProcedureDefinition("", "t", Array.Empty<StepDefinition>()));
        Assert.Throws<ArgumentNullException>(() => new ProcedureDefinition("p", "t", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcedureDefinition("p", "t", Array.Empty<StepDefinition>(), timeLimitSeconds: -1f));

        var definition = new ProcedureDefinition("p", null, Array.Empty<StepDefinition>());
        Assert.That(definition.Title, Is.EqualTo("p"));
        Assert.That(definition.SafetyRules, Is.Empty);
        Assert.That(definition.Weights, Is.SameAs(ScoringWeights.Default));
    }

    [Test]
    public void StepFactories_SetKindSpecificFields()
    {
        var inspect = StepDefinition.Inspect("i", "I", "part", 2f);
        var operate = StepDefinition.Operate("o", "O", "valve", "closed");
        var tool = StepDefinition.Tool("t", "T", "socket", "wrench");
        var measure = StepDefinition.Measure("m", "M", "gauge", 1f, 0.5f, null);
        var confirm = StepDefinition.Confirm("c", null);

        Assert.That((inspect.Kind, inspect.PartId, inspect.DwellSeconds), Is.EqualTo((StepKind.Inspect, "part", 2f)));
        Assert.That((operate.Kind, operate.TargetState), Is.EqualTo((StepKind.Operate, "closed")));
        Assert.That((tool.Kind, tool.PartId, tool.ToolId), Is.EqualTo((StepKind.Tool, "socket", "wrench")));
        Assert.That((measure.Kind, measure.ExpectedValue, measure.Tolerance, measure.Unit), Is.EqualTo((StepKind.Measure, 1f, 0.5f, "")));
        Assert.That((confirm.Kind, confirm.Title, confirm.PartId), Is.EqualTo((StepKind.Confirm, "c", (string)null)));
        Assert.That(tool.ToString(), Is.EqualTo("Tool 't'"));
    }

    [Test]
    public void StepFactories_RejectMissingParameters()
    {
        Assert.Throws<ArgumentException>(() => StepDefinition.Confirm(" ", "x"));
        Assert.Throws<ArgumentException>(() => StepDefinition.Inspect("i", "I", null, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => StepDefinition.Inspect("i", "I", "p", 0f));
        Assert.Throws<ArgumentException>(() => StepDefinition.Operate("o", "O", "p", ""));
        Assert.Throws<ArgumentException>(() => StepDefinition.Tool("t", "T", "s", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => StepDefinition.Measure("m", "M", "g", 0f, -1f, "bar"));
    }

    [Test]
    public void SafetyRule_ValidatesAndDescribes()
    {
        var rule = new SafetyRule("r", null, "valve", "breaker", "locked");

        Assert.That(rule.Description, Is.EqualTo("r"));
        Assert.That(rule.ToString(), Is.EqualTo("r: breaker must be locked before valve"));
        Assert.Throws<ArgumentException>(() => new SafetyRule("r", "d", "", "b", "s"));
    }

    [Test]
    public void InteractionEvent_FactoriesAndPhysicalActions()
    {
        Assert.That(InteractionEvent.State(1, "p", "s").IsPhysicalAction, Is.True);
        Assert.That(InteractionEvent.Socketed(1, "s", "t").IsPhysicalAction, Is.True);
        Assert.That(InteractionEvent.Gaze(1, "p", 1f).IsPhysicalAction, Is.False);
        Assert.That(InteractionEvent.Measured(1, 2f).Number, Is.EqualTo(2f));
        Assert.That(InteractionEvent.Confirmed(3).Time, Is.EqualTo(3));
        Assert.That(InteractionEvent.Help(1).Kind, Is.EqualTo(InteractionKind.HelpRequested));
        Assert.That(InteractionEvent.State(1, "p", "s").ToString(), Does.Contain("StateChanged"));
    }
}
