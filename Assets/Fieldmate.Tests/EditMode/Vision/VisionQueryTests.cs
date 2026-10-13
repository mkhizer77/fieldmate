using System.Linq;
using Fieldmate.Knowledge;
using Fieldmate.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Vision;

/// <summary>#22: candidates from the gaze cone, a JSON-only prompt, and a parser that survives real model output.</summary>
public class VisionQueryTests
{
    private static readonly PartInfo Valve = new("relief_valve", "relief valve", "Opens above 6 bar to protect the line.", "");
    private static readonly PartInfo Motor = new("motor", "motor", "Drives the pump.", "");
    private static readonly Ray Gaze = new(Vector3.zero, Vector3.forward);

    [Test]
    public void Candidates_are_the_gazed_part_then_parts_in_the_cone_nearest_first()
    {
        var parts = new[]
        {
            new PartPoint("motor", new Vector3(0.3f, 0f, 2f)),        // 8.5°
            new PartPoint("gauge", new Vector3(0.05f, 0f, 2f)),       // 1.4°
            new PartPoint("inlet_valve", new Vector3(2f, 0f, 2f)),    // 45°, outside
            new PartPoint("relief_valve", new Vector3(0f, 0f, 1f)),   // the gaze hits it
            new PartPoint("behind", new Vector3(0f, 0f, -1f)),
        };

        var ids = VisionQuery.Candidates(parts, Gaze, "relief_valve");

        Assert.That(ids, Is.EqualTo(new[] { "relief_valve", "gauge", "motor" }));
    }

    [Test]
    public void Candidates_are_capped_and_work_without_a_gazed_part()
    {
        var parts = Enumerable.Range(0, 10).Select(i => new PartPoint($"p{i}", new Vector3(i * 0.01f, 0f, 1f))).ToArray();

        var ids = VisionQuery.Candidates(parts, Gaze, null, max: 4);

        Assert.That(ids, Is.EqualTo(new[] { "p0", "p1", "p2", "p3" }));
        Assert.That(VisionQuery.Candidates(new PartPoint[0], Gaze, "motor"), Is.EqualTo(new[] { "motor" }));
    }

    [Test]
    public void The_prompt_lists_the_candidates_the_gaze_and_the_json_schema()
    {
        var prompt = VisionQuery.BuildPrompt(new[] { Valve, Motor }, new Vector2(0.55f, 0.4f), "Remove the relief valve", "en");

        StringAssert.Contains("- relief_valve: relief valve: Opens above 6 bar", prompt);
        StringAssert.Contains("- motor: motor:", prompt);
        StringAssert.Contains("x=0.55, y=0.40", prompt);
        StringAssert.Contains("Current procedure step: Remove the relief valve", prompt);
        StringAssert.Contains("Never invent a part_id", prompt);
        StringAssert.Contains("JSON object only", prompt);
        StringAssert.Contains("\"bbox\"", prompt);
        StringAssert.Contains("in English", prompt);
    }

    [Test]
    public void The_prompt_without_gaze_or_candidates_asks_about_the_centre_and_forbids_part_ids()
    {
        var prompt = VisionQuery.BuildPrompt(new PartInfo[0], null, null, "de");

        StringAssert.Contains("centre of the image", prompt);
        StringAssert.Contains("set part_id to null", prompt);
        StringAssert.Contains("in German", prompt);
        StringAssert.DoesNotContain("procedure step", prompt);
    }

    [Test]
    public void Parses_a_clean_answer()
    {
        const string text = "{\"label\":\"relief valve\",\"part_id\":\"relief_valve\",\"confidence\":0.82,\"bbox\":[0.4,0.3,0.6,0.5],\"one_line_help\":\"Vents above 6 bar.\"}";

        Assert.That(VisionQuery.TryParseAnswer(text, out var answer, out var error), Is.True, error);
        Assert.That(answer.Label, Is.EqualTo("relief valve"));
        Assert.That(answer.PartId, Is.EqualTo("relief_valve"));
        Assert.That(answer.Confidence, Is.EqualTo(0.82f).Within(1e-6f));
        Assert.That(answer.Box.Value.Center.x, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(answer.Help, Is.EqualTo("Vents above 6 bar."));
    }

    [Test]
    public void Parses_an_answer_inside_a_code_fence_with_prose_around_it()
    {
        const string text = "Sure! Here it is:\n```json\n{\"label\": \"a torque wrench\", \"part_id\": null, \"confidence\": 0.7, \"bbox\": null, \"one_line_help\": \"\"}\n```";

        Assert.That(VisionQuery.TryParseAnswer(text, out var answer, out _), Is.True);
        Assert.That(answer.PartId, Is.Null);
        Assert.That(answer.Box, Is.Null);
    }

    [TestCase("", "empty")]
    [TestCase("I can't tell what that is.", "no JSON object")]
    [TestCase("{\"label\": \"x\", }", "unreadable JSON")]
    [TestCase("{\"part_id\":\"motor\",\"confidence\":0.9}", "no label")]
    [TestCase("{\"label\":\"motor\",\"confidence\":\"high\"}", "no confidence")]
    public void Rejects_answers_without_a_label_and_a_numeric_confidence(string text, string expected)
    {
        Assert.That(VisionQuery.TryParseAnswer(text, out var answer, out var error), Is.False);
        Assert.That(answer, Is.Null);
        StringAssert.Contains(expected, error);
    }

    [Test]
    public void A_malformed_box_is_dropped_but_the_answer_kept()
    {
        Assert.That(VisionQuery.TryParseAnswer("{\"label\":\"motor\",\"confidence\":0.9,\"bbox\":[0.1,0.2,0.3]}", out var three, out _), Is.True);
        Assert.That(three.Box, Is.Null);
        Assert.That(VisionQuery.TryParseAnswer("{\"label\":\"motor\",\"confidence\":0.9,\"bbox\":{\"x\":1}}", out var obj, out _), Is.True);
        Assert.That(obj.Box, Is.Null);
    }
}
