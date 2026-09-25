using System;
using Fieldmate.Procedures;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Procedures;

public class ScoringTests
{
    private static readonly ScoringWeights W = ScoringWeights.Default;

    [TestCase(0, 0, 0, 100)]
    [TestCase(1, 0, 0, 95)]
    [TestCase(0, 1, 0, 75)]
    [TestCase(0, 0, 3, 94)]
    [TestCase(10, 3, 10, 0)]
    public void Score_AppliesPenaltiesAndClamps(int errors, int violations, int help, int expected)
    {
        Assert.That(Scoring.Score(W, 60, 600f, errors, violations, help), Is.EqualTo(expected));
    }

    [Test]
    public void Score_PenalisesOvertimePerMinute_OnlyWithALimit()
    {
        Assert.That(Scoring.Score(W, 720, 600f, 0, 0, 0), Is.EqualTo(90));
        Assert.That(Scoring.Score(W, 720, 0f, 0, 0, 0), Is.EqualTo(100));
    }

    [Test]
    public void Passed_NeedsPassScoreAndNoViolations()
    {
        Assert.That(Scoring.Passed(W, 60, 0), Is.True);
        Assert.That(Scoring.Passed(W, 59, 0), Is.False);
        Assert.That(Scoring.Passed(W, 100, 1), Is.False);
    }

    [Test]
    public void NullWeights_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Scoring.Score(null, 0, 0f, 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => Scoring.Passed(null, 0, 0));
    }

    [Test]
    public void ProcedureError_Formats()
    {
        Assert.That(new ProcedureError(1, "step", "msg").ToString(), Is.EqualTo("[step] msg"));
    }

    [Test]
    public void Overtime_FlowsIntoRunnerResult()
    {
        var runner = new ProcedureRunner(ReliefValveProcedure.Create(timeLimitSeconds: 10f));
        runner.Start(0);
        foreach (var e in ReliefValveProcedure.HappyPath())
        {
            runner.Handle(e);
        }

        // 70 s against a 10 s limit: 1 minute over, 5 points.
        Assert.That(runner.Result.Score, Is.EqualTo(95));
    }
}
