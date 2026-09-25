using System;
using System.Collections.Generic;

namespace Fieldmate.Procedures;

/// <summary>Penalties that turn a run into a 0–100 score.</summary>
public sealed class ScoringWeights
{
    public ScoringWeights(float errorPenalty, float violationPenalty, float helpPenalty, float overtimePenaltyPerMinute,
        int passScore)
    {
        ErrorPenalty = errorPenalty;
        ViolationPenalty = violationPenalty;
        HelpPenalty = helpPenalty;
        OvertimePenaltyPerMinute = overtimePenaltyPerMinute;
        PassScore = passScore;
    }

    public static ScoringWeights Default { get; } = new(5f, 25f, 2f, 5f, 60);

    public float ErrorPenalty { get; }
    public float ViolationPenalty { get; }

    /// <summary>Per help request: assistant reliance is scored, lightly.</summary>
    public float HelpPenalty { get; }

    public float OvertimePenaltyPerMinute { get; }
    public int PassScore { get; }
}

/// <summary>A mistake that did not endanger anyone (wrong tool, wrong reading, out-of-order step).</summary>
public readonly struct ProcedureError
{
    public ProcedureError(double time, string stepId, string message)
    {
        Time = time;
        StepId = stepId;
        Message = message;
    }

    public double Time { get; }
    public string StepId { get; }
    public string Message { get; }

    public override string ToString() => $"[{StepId}] {Message}";
}

/// <summary>Debrief data for one completed run.</summary>
public sealed class ProcedureResult
{
    public ProcedureResult(string procedureId, double totalSeconds, IReadOnlyList<double> stepSeconds,
        IReadOnlyList<ProcedureError> errors, IReadOnlyList<SafetyRule> violations, int helpRequests, int score, bool passed)
    {
        ProcedureId = procedureId;
        TotalSeconds = totalSeconds;
        StepSeconds = stepSeconds;
        Errors = errors;
        Violations = violations;
        HelpRequests = helpRequests;
        Score = score;
        Passed = passed;
    }

    public string ProcedureId { get; }
    public double TotalSeconds { get; }
    public IReadOnlyList<double> StepSeconds { get; }
    public IReadOnlyList<ProcedureError> Errors { get; }
    public IReadOnlyList<SafetyRule> Violations { get; }
    public int HelpRequests { get; }

    /// <summary>0–100.</summary>
    public int Score { get; }

    /// <summary>Score at or above the pass mark and no safety violations.</summary>
    public bool Passed { get; }
}

public static class Scoring
{
    public static int Score(ScoringWeights weights, double totalSeconds, float timeLimitSeconds, int errors,
        int violations, int helpRequests)
    {
        if (weights == null)
        {
            throw new ArgumentNullException(nameof(weights));
        }

        var penalty = errors * weights.ErrorPenalty + violations * weights.ViolationPenalty + helpRequests * weights.HelpPenalty;
        if (timeLimitSeconds > 0f && totalSeconds > timeLimitSeconds)
        {
            penalty += (float)((totalSeconds - timeLimitSeconds) / 60d) * weights.OvertimePenaltyPerMinute;
        }

        var score = (int)Math.Round(100f - penalty, MidpointRounding.AwayFromZero);
        return score < 0 ? 0 : score > 100 ? 100 : score;
    }

    public static bool Passed(ScoringWeights weights, int score, int violations) =>
        violations == 0 && score >= (weights ?? throw new ArgumentNullException(nameof(weights))).PassScore;
}
