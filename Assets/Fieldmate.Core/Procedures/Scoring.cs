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

    /// <summary>
    /// How the score came about, for the assistant's context and the offline answers (device test 2026-10-01: "86 out
    /// of 100" and the mate couldn't say why): the verdict, the time against the limit, and every deduction with what
    /// caused it. Each line of the sum is spelled out so the model can explain it without arithmetic of its own.
    /// </summary>
    public static string Explain(ProcedureResult result, ScoringWeights weights, float timeLimitSeconds)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        weights ??= ScoringWeights.Default;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var parts = new System.Collections.Generic.List<string>();
        if (result.Violations.Count > 0)
        {
            var names = string.Join("; ", System.Linq.Enumerable.Select(result.Violations, v => v.Description));
            parts.Add(string.Format(c, "{0} safety violation{1} -{2:0} ({3})", result.Violations.Count, result.Violations.Count == 1 ? "" : "s",
                result.Violations.Count * weights.ViolationPenalty, names));
        }

        if (result.Errors.Count > 0)
        {
            var names = string.Join("; ", System.Linq.Enumerable.Select(result.Errors, e => e.Message));
            parts.Add(string.Format(c, "{0} mistake{1} -{2:0} ({3})", result.Errors.Count, result.Errors.Count == 1 ? "" : "s",
                result.Errors.Count * weights.ErrorPenalty, names));
        }

        if (result.HelpRequests > 0)
        {
            parts.Add(string.Format(c, "{0} question{1} to the assistant during the run -{2:0} ({3:0} each)", result.HelpRequests,
                result.HelpRequests == 1 ? "" : "s", result.HelpRequests * weights.HelpPenalty, weights.HelpPenalty));
        }

        var over = timeLimitSeconds > 0f ? result.TotalSeconds - timeLimitSeconds : 0d;
        if (over > 0d)
        {
            parts.Add(string.Format(c, "{0:0.#} min over the time limit -{1:0}", over / 60d, over / 60d * weights.OvertimePenaltyPerMinute));
        }

        var time = timeLimitSeconds > 0f
            ? string.Format(c, "took {0}, limit {1}", Duration(result.TotalSeconds), Duration(timeLimitSeconds))
            : string.Format(c, "took {0}", Duration(result.TotalSeconds));
        var verdict = result.Passed
            ? "passed"
            : result.Violations.Count > 0 ? "not passed (any safety violation fails the run)" : string.Format(c, "not passed (pass mark {0})", weights.PassScore);
        var deductions = parts.Count == 0 ? "no deductions" : "100, " + string.Join(", ", parts);
        return string.Format(c, "score {0}/100, {1}; {2}; {3}.", result.Score, verdict, time, deductions);
    }

    private static string Duration(double seconds)
    {
        var s = (int)Math.Round(seconds);
        return s >= 60 ? $"{s / 60} min {s % 60} s" : $"{s} s";
    }

    public static bool Passed(ScoringWeights weights, int score, int violations) =>
        violations == 0 && score >= (weights ?? throw new ArgumentNullException(nameof(weights))).PassScore;
}
