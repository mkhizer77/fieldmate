using System;
using System.Collections.Generic;

namespace Fieldmate.Procedures;

/// <summary>
/// An ordered, validated procedure (design.md §5.3). Authored as a ScriptableObject in Unity and converted to this plain
/// type, so the runner stays testable without the engine.
/// </summary>
public sealed class ProcedureDefinition
{
    public ProcedureDefinition(string id, string title, IEnumerable<StepDefinition> steps,
        IEnumerable<SafetyRule> safetyRules = null, float timeLimitSeconds = 0f, ScoringWeights weights = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Procedure id is required.", nameof(id)) : id;
        Title = title ?? id;
        Steps = new List<StepDefinition>(steps ?? throw new ArgumentNullException(nameof(steps)));
        SafetyRules = new List<SafetyRule>(safetyRules ?? Array.Empty<SafetyRule>());
        TimeLimitSeconds = timeLimitSeconds >= 0f ? timeLimitSeconds : throw new ArgumentOutOfRangeException(nameof(timeLimitSeconds));
        Weights = weights ?? ScoringWeights.Default;
    }

    public string Id { get; }
    public string Title { get; }
    public IReadOnlyList<StepDefinition> Steps { get; }
    public IReadOnlyList<SafetyRule> SafetyRules { get; }

    /// <summary>Seconds before overtime penalties start; 0 means no limit.</summary>
    public float TimeLimitSeconds { get; }

    public ScoringWeights Weights { get; }

    /// <summary>Authoring problems; empty when the procedure can run.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (Steps.Count == 0)
        {
            problems.Add("Procedure has no steps.");
        }

        var ids = new HashSet<string>();
        for (var i = 0; i < Steps.Count; i++)
        {
            if (Steps[i] == null)
            {
                problems.Add($"Step {i + 1} is missing.");
            }
            else if (!ids.Add(Steps[i].Id))
            {
                problems.Add($"Duplicate step id '{Steps[i].Id}'.");
            }
        }

        var ruleIds = new HashSet<string>();
        foreach (var rule in SafetyRules)
        {
            if (rule == null)
            {
                problems.Add("A safety rule is missing.");
            }
            else if (!ruleIds.Add(rule.Id))
            {
                problems.Add($"Duplicate safety rule id '{rule.Id}'.");
            }
        }

        return problems;
    }
}
