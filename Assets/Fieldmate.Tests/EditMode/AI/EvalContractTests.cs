using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>
/// #17: the AI eval fixture (Tests/AIEval/utterances.json) is well-formed, every case routes through the real session,
/// tools and scorer with a mocked model that answers as expected, and the scorer catches the ways a model goes wrong.
/// The same harness runs the real model from Fieldmate → Run AI Eval.
/// </summary>
public class EvalContractTests
{
    private static string FixturePath => Path.Combine(Application.dataPath, "Fieldmate.Tests", "AIEval", "utterances.json");

    private static MachineManual Manual() =>
        MachineManual.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Manual", "manual.json")));

    [Test]
    public void Fixture_has_25_wellformed_cases_against_the_real_tools_and_steps()
    {
        var cases = EvalCase.ParseFixture(File.ReadAllText(FixturePath));
        Assert.That(cases, Has.Count.EqualTo(25));
        Assert.That(cases.Select(c => c.Id), Is.Unique);

        var registry = FieldmateTools.CreateRegistry(includeVision: false);
        var steps = DemoProcedures.ReliefValveReplacement().Steps.Select(s => s.Id).ToList();
        foreach (var c in cases)
        {
            Assert.That(c.Tools.Count + c.Keywords.Count, Is.GreaterThan(0), $"{c.Id} checks something");
            foreach (var tool in c.Tools.SelectMany(t => t.Split('|')).Concat(c.Forbid))
            {
                Assert.That(registry.TryGetDefinition(tool, out _), Is.True, $"{c.Id}: unknown tool {tool}");
            }

            foreach (var tool in c.Args.Keys)
            {
                Assert.That(registry.TryValidate(new ToolCall("check", tool, c.ArgumentsJson(tool)), out _, out var error), Is.True, $"{c.Id}: {error}");
            }

            if (c.Step != null)
            {
                Assert.That(steps, Does.Contain(c.Step), $"{c.Id}: unknown step");
            }
        }
    }

    [Test]
    public async Task Every_case_passes_with_a_model_that_answers_as_expected()
    {
        var cases = EvalCase.ParseFixture(File.ReadAllText(FixturePath));
        var harness = new EvalHarness(Manual(), c => new ExpectedModel(c));
        foreach (var c in cases)
        {
            var result = await harness.RunAsync(c, CancellationToken.None);
            Assert.That(result.Failures, Is.Empty, $"{c.Id}: {string.Join("; ", result.Failures)} (calls: {string.Join("; ", result.ToolCalls)})");
        }
    }

    [Test]
    public void Scorer_catches_missing_and_forbidden_tools_wrong_arguments_and_missing_words()
    {
        var c = new EvalCase("x", "Where is the relief valve?", null, new[] { "highlight_part" },
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>>
            {
                ["highlight_part"] = new System.Collections.Generic.Dictionary<string, string> { ["part_id"] = "relief_valve" },
            },
            new[] { "start_procedure" }, new[] { "relief", "left|right" });

        Assert.That(EvalScorer.Score(c, new[] { "highlight_part(part_id=relief_valve)" }, "The relief valve is on the right."), Is.Empty);
        Assert.That(EvalScorer.Score(c, Array.Empty<string>(), "The relief valve is on the right."), Does.Contain("expected a highlight_part call"));
        Assert.That(EvalScorer.Score(c, new[] { "highlight_part(part_id=pump)" }, "The relief valve is on the right."),
            Does.Contain("expected highlight_part(part_id=relief_valve)"));
        Assert.That(EvalScorer.Score(c, new[] { "highlight_part(part_id=relief_valve)", "start_procedure(procedure_id=x)" }, "relief right"),
            Does.Contain("must not call start_procedure"));
        Assert.That(EvalScorer.Score(c, new[] { "highlight_part(part_id=relief_valve)" }, "It is over there."),
            Is.EquivalentTo(new[] { "answer lacks \"relief\"", "answer lacks \"left|right\"" }));
    }

    [Test]
    public void Advancing_to_a_step_completes_the_ones_before_it()
    {
        var runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        runner.SetInitialState("main_breaker", "on");
        runner.SetInitialState("inlet_valve", "open");
        runner.SetInitialState("pump_cover", "fitted");
        var clock = 0d;
        EvalHarness.AdvanceTo(runner, "replace", ref clock);
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("replace"));
        Assert.That(runner.Errors, Is.Empty);
        Assert.That(runner.Violations, Is.Empty);
    }

    [Test]
    public void Report_lists_every_case_with_its_verdict()
    {
        var pass = new EvalResult(new EvalCase("a", "hi", null, null, null, null, new[] { "x" }), new[] { "read_telemetry()" }, "x", Array.Empty<string>(), 1.5);
        var fail = new EvalResult(new EvalCase("b", "yo", "lockout", null, null, null, new[] { "y" }), Array.Empty<string>(), "no", new[] { "answer lacks \"y\"" }, 2.5);
        var md = EvalReport.Markdown(new[] { pass, fail }, "claude-test", new DateTime(2026, 10, 1, 14, 0, 0));
        Assert.That(md, Does.StartWith("# AI eval: 1/2 passed"));
        Assert.That(md, Does.Contain("Median turn 2.0 s"));
        Assert.That(md, Does.Contain("| ✅ | `a` | — | read_telemetry() |  |"));
        Assert.That(md, Does.Contain("| ❌ | `b` | lockout |  | answer lacks \"y\" |"));
    }

    /// <summary>Answers each case the way the fixture expects: its tools in one response, then a sentence with its words.</summary>
    private sealed class ExpectedModel : IChatModel
    {
        private readonly EvalCase evalCase;
        private bool called;

        public ExpectedModel(EvalCase evalCase) => this.evalCase = evalCase;

        public string Name => "expected";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            var words = string.Join(" ", evalCase.Keywords.Select(k => k.Split('|')[0]));
            if (!called && evalCase.Tools.Count > 0)
            {
                called = true;
                var calls = evalCase.Tools.Select((t, i) => t.Split('|')[0]).Select((t, i) => new ToolCall($"c{i}", t, evalCase.ArgumentsJson(t))).ToArray();
                return Task.FromResult(new ChatResponse("One moment.", calls, StopReason.ToolUse));
            }

            return Task.FromResult(new ChatResponse($"Done: {words}.", null, StopReason.EndTurn));
        }
    }
}
