using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Fieldmate.Json;
using Fieldmate.Knowledge;
using UnityEngine;

namespace Fieldmate.Vision;

/// <summary>A twin part as a candidate answer: where along the gaze it is.</summary>
public readonly struct PartPoint
{
    public PartPoint(string partId, Vector3 point)
    {
        PartId = partId;
        Point = point;
    }

    public string PartId { get; }

    /// <summary>The part's point nearest the gaze ray (or its centre).</summary>
    public Vector3 Point { get; }
}

/// <summary>
/// Builds the vision question (design.md §5.5 step 2) and reads the JSON answer (step 3). Candidates from the twin
/// narrow the label space: the model picks among parts the user could be looking at instead of naming freely.
/// </summary>
public static class VisionQuery
{
    public const float CandidateConeDegrees = 20f;
    public const int MaxCandidates = 6;

    /// <summary>
    /// Parts within <paramref name="coneDegrees"/> of the gaze, nearest the gaze first; the part the gaze ray hits leads.
    /// </summary>
    public static List<string> Candidates(IEnumerable<PartPoint> parts, Ray gaze, string gazedPartId,
        float coneDegrees = CandidateConeDegrees, int max = MaxCandidates)
    {
        var scored = new List<(float angle, string id)>();
        foreach (var part in parts ?? throw new ArgumentNullException(nameof(parts)))
        {
            if (string.IsNullOrEmpty(part.PartId) || part.PartId == gazedPartId)
            {
                continue;
            }

            var to = part.Point - gaze.origin;
            if (to.sqrMagnitude < 1e-6f)
            {
                continue;
            }

            var angle = Vector3.Angle(gaze.direction, to);
            if (angle <= coneDegrees)
            {
                scored.Add((angle, part.PartId));
            }
        }

        scored.Sort((a, b) => a.angle.CompareTo(b.angle));
        var result = new List<string>(Math.Min(max, scored.Count + 1));
        if (!string.IsNullOrEmpty(gazedPartId))
        {
            result.Add(gazedPartId);
        }

        foreach (var (_, id) in scored)
        {
            if (result.Count >= max)
            {
                break;
            }

            if (!result.Contains(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    /// <param name="gazePoint">Where the user looked, in [0, 1]² of the image, or null when unknown.</param>
    /// <param name="stepTitle">The current procedure step, or null.</param>
    public static string BuildPrompt(IReadOnlyList<PartInfo> candidates, Vector2? gazePoint, string stepTitle, string languageCode)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("This is one frame from the passthrough camera of a mixed-reality headset. The user is a maintenance ")
            .Append("technician at an industrial pump skid and asked \"What's this?\" about what they are looking at.\n");
        sb.Append(gazePoint is { } g
            ? string.Format(c, "Their gaze is at x={0:0.00}, y={1:0.00} of the image (0..1, origin top-left); identify the object there.\n", g.x, g.y)
            : "Identify the object at the centre of the image.\n");
        if (!string.IsNullOrWhiteSpace(stepTitle))
        {
            sb.Append("Current procedure step: ").Append(stepTitle.Trim()).Append('\n');
        }

        if (candidates is { Count: > 0 })
        {
            sb.Append("Machine parts near the gaze (part_id: name: description):\n");
            foreach (var part in candidates)
            {
                sb.Append("- ").Append(part.Id).Append(": ").Append(part.Name).Append(": ").Append(Shorten(part.Description, 120)).Append('\n');
            }

            sb.Append("If the object is one of these parts, set part_id to its id. If it is something else (a tool, a hand, ")
                .Append("furniture), set part_id to null and name it plainly. Never invent a part_id.\n");
        }
        else
        {
            sb.Append("No machine part is near the gaze; set part_id to null.\n");
        }

        sb.Append(languageCode == "de"
            ? "Write label and one_line_help in German.\n"
            : "Write label and one_line_help in English.\n");
        sb.Append("Answer with one JSON object only, no prose, no code fence:\n")
            .Append("{\"label\": string, \"part_id\": string or null, \"confidence\": number 0..1, ")
            .Append("\"bbox\": [x_min, y_min, x_max, y_max] in 0..1 of the image or null, \"one_line_help\": string of at most 15 words}\n")
            .Append("confidence is how sure you are of the label; below 0.5 means a guess.");
        return sb.ToString();
    }

    /// <summary>
    /// Reads the model's answer. Tolerates a code fence or stray prose around the object; anything without a label and a
    /// confidence is an error, so the caller falls back to the twin.
    /// </summary>
    public static bool TryParseAnswer(string text, out VisionAnswer answer, out string error)
    {
        answer = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty answer";
            return false;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            error = "no JSON object in the answer";
            return false;
        }

        if (!JsonReader.TryParse(text.Substring(start, end - start + 1), out var json, out var parseError) || json.Kind != JsonKind.Object)
        {
            error = $"unreadable JSON ({parseError?.Message ?? "not an object"})";
            return false;
        }

        if (!json["label"].TryGetString(out var label) || string.IsNullOrWhiteSpace(label))
        {
            error = "the answer has no label";
            return false;
        }

        if (!json["confidence"].TryGetNumber(out var confidence))
        {
            error = "the answer has no confidence";
            return false;
        }

        answer = new VisionAnswer(label, json["part_id"].AsString(), (float)confidence, ReadBox(json["bbox"]), json["one_line_help"].AsString(string.Empty));
        error = null;
        return true;
    }

    private static NormalizedBox? ReadBox(JsonValue value)
    {
        if (value.Kind != JsonKind.Array || value.Items.Count != 4)
        {
            return null;
        }

        var v = new float[4];
        for (var i = 0; i < 4; i++)
        {
            if (!value[i].TryGetNumber(out var n))
            {
                return null;
            }

            v[i] = (float)n;
        }

        return new NormalizedBox(v[0], v[1], v[2], v[3]);
    }

    private static string Shorten(string text, int max)
    {
        text = (text ?? string.Empty).Trim();
        return text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…";
    }
}
