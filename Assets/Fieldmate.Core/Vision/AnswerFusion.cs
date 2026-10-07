using System;
using Fieldmate.Knowledge;
using UnityEngine;

namespace Fieldmate.Vision;

/// <summary>An axis-aligned box in [0, 1]² of the image, top-left origin, as the vision model reports it.</summary>
public readonly struct NormalizedBox
{
    public NormalizedBox(float xMin, float yMin, float xMax, float yMax)
    {
        XMin = xMin;
        YMin = yMin;
        XMax = xMax;
        YMax = yMax;
    }

    public float XMin { get; }
    public float YMin { get; }
    public float XMax { get; }
    public float YMax { get; }

    public Vector2 Center => new((XMin + XMax) * 0.5f, (YMin + YMax) * 0.5f);

    /// <summary>Inside the image, non-empty and ordered; anything else is a model mistake and is ignored.</summary>
    public bool IsValid =>
        XMin >= 0f && YMin >= 0f && XMax <= 1f && YMax <= 1f && XMax - XMin > 1e-3f && YMax - YMin > 1e-3f;
}

/// <summary>What the vision model said about one frame (design.md §5.5 step 3).</summary>
public sealed class VisionAnswer
{
    public VisionAnswer(string label, string partId, float confidence, NormalizedBox? box, string help)
    {
        Label = label?.Trim() ?? string.Empty;
        PartId = string.IsNullOrWhiteSpace(partId) ? null : partId.Trim();
        Confidence = Mathf.Clamp01(float.IsNaN(confidence) ? 0f : confidence);
        Box = box;
        Help = help?.Trim() ?? string.Empty;
    }

    public string Label { get; }

    /// <summary>A part id from the candidates, or null when the model saw something else.</summary>
    public string PartId { get; }

    public float Confidence { get; }
    public NormalizedBox? Box { get; }
    public string Help { get; }
}

public enum AnswerSource
{
    /// <summary>Nothing to say: no usable answer and no part under the gaze.</summary>
    None,

    /// <summary>The vision model's answer.</summary>
    Vision,

    /// <summary>The twin's own part data, used when the model was unsure or failed.</summary>
    ModelData,
}

/// <summary>The answer the user gets: what it is, how sure, and where to pin the label.</summary>
public sealed class FusedAnswer
{
    public static readonly FusedAnswer Nothing = new(AnswerSource.None, string.Empty, null, string.Empty, 0f, null);

    public FusedAnswer(AnswerSource source, string label, string partId, string help, float confidence, NormalizedBox? box)
    {
        Source = source;
        Label = label ?? string.Empty;
        PartId = partId;
        Help = help ?? string.Empty;
        Confidence = confidence;
        Box = box;
    }

    public AnswerSource Source { get; }
    public string Label { get; }
    public string PartId { get; }
    public string Help { get; }
    public float Confidence { get; }

    /// <summary>Where the label goes: the box centre when set, otherwise the gaze point.</summary>
    public NormalizedBox? Box { get; }

    /// <summary>A vision answer under the confidence threshold with no twin part to fall back on.</summary>
    public bool IsUnsure => Source == AnswerSource.Vision && Confidence < AnswerFusion.ConfidenceThreshold;

    /// <summary>The tool result the assistant reads back, one or two short sentences.</summary>
    public string Describe()
    {
        var help = string.IsNullOrEmpty(Help) ? string.Empty : " " + Help;
        return Source switch
        {
            AnswerSource.ModelData => $"That's the {Label} (from model data).{help}",
            AnswerSource.Vision when IsUnsure => $"Not sure; it might be {Label}.{help}",
            AnswerSource.Vision when PartId != null => $"That's the {Label}.{help}",
            AnswerSource.Vision => $"That looks like {Label}; it isn't part of the machine.{help}",
            _ => "Couldn't make out what you're looking at. Look straight at the part and ask again.",
        };
    }
}

/// <summary>
/// Combines the vision model's answer with the twin's metadata (design.md §5.5 step 5): the model can be wrong, the twin
/// knows its parts, so an unsure model yields to the part under the user's gaze.
/// </summary>
public static class AnswerFusion
{
    public const float ConfidenceThreshold = 0.5f;

    /// <param name="answer">The model's answer, or null when the request failed.</param>
    /// <param name="gazePartId">The twin part the gaze ray hits, or null.</param>
    public static FusedAnswer Fuse(VisionAnswer answer, string gazePartId, PartCatalog catalog)
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        // An id the twin doesn't know is a hallucination: keep the words, drop the id.
        var visionPart = answer?.PartId != null && catalog.TryGet(answer.PartId, out var seen) ? seen : null;
        var box = answer?.Box is { IsValid: true } b ? b : (NormalizedBox?)null;
        var confident = answer != null && answer.Confidence >= ConfidenceThreshold;

        if (confident && visionPart != null)
        {
            return new FusedAnswer(AnswerSource.Vision, visionPart.Name, visionPart.Id, HelpOr(answer.Help, visionPart),
                answer.Confidence, box);
        }

        if (confident && answer.Label.Length > 0)
        {
            return new FusedAnswer(AnswerSource.Vision, answer.Label, null, answer.Help, answer.Confidence, box);
        }

        if (catalog.TryGet(gazePartId, out var gazed))
        {
            // The label goes where the user looked, not on the unsure model's box.
            return new FusedAnswer(AnswerSource.ModelData, gazed.Name, gazed.Id, gazed.Description, answer?.Confidence ?? 0f, null);
        }

        if (answer != null && (visionPart != null || answer.Label.Length > 0))
        {
            return new FusedAnswer(AnswerSource.Vision, visionPart?.Name ?? answer.Label, visionPart?.Id,
                answer.Help, answer.Confidence, box);
        }

        return FusedAnswer.Nothing;
    }

    private static string HelpOr(string help, PartInfo part) => string.IsNullOrEmpty(help) ? part.Description : help;
}
