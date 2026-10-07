using Fieldmate.Knowledge;
using Fieldmate.Vision;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Vision;

/// <summary>#21: the vision model's answer is fused with the twin's part data; an unsure model yields to the twin.</summary>
public class AnswerFusionTests
{
    private static readonly PartCatalog Catalog = new(new[]
    {
        new PartInfo("relief_valve", "relief valve", "Opens above 6 bar to protect the line.", ""),
        new PartInfo("motor", "motor", "Drives the pump.", "Isolate before opening."),
    });

    private static readonly NormalizedBox Box = new(0.4f, 0.3f, 0.6f, 0.5f);

    [Test]
    public void A_confident_answer_naming_a_twin_part_uses_the_twin_name_and_the_box()
    {
        var fused = AnswerFusion.Fuse(new VisionAnswer("Relief Valve (brass)", "relief_valve", 0.9f, Box, "Vents excess pressure."), "motor", Catalog);

        Assert.That(fused.Source, Is.EqualTo(AnswerSource.Vision));
        Assert.That(fused.PartId, Is.EqualTo("relief_valve"));
        Assert.That(fused.Label, Is.EqualTo("relief valve"), "the twin's canonical name, not the model's wording");
        Assert.That(fused.Help, Is.EqualTo("Vents excess pressure."));
        Assert.That(fused.Box, Is.EqualTo(Box), "a confident answer pins its label on the box, even against the gaze");
    }

    [Test]
    public void A_low_confidence_answer_yields_to_the_part_under_the_gaze()
    {
        var fused = AnswerFusion.Fuse(new VisionAnswer("relief valve", "relief_valve", 0.3f, Box, "Vents pressure."), "motor", Catalog);

        Assert.That(fused.Source, Is.EqualTo(AnswerSource.ModelData));
        Assert.That(fused.PartId, Is.EqualTo("motor"));
        Assert.That(fused.Label, Is.EqualTo("motor"));
        Assert.That(fused.Help, Is.EqualTo("Drives the pump."), "the twin's description");
        Assert.That(fused.Box, Is.Null, "the label goes where the user looked, not on the unsure box");
        StringAssert.Contains("from model data", fused.Describe());
    }

    [Test]
    public void The_threshold_is_inclusive_at_one_half()
    {
        var fused = AnswerFusion.Fuse(new VisionAnswer("relief valve", "relief_valve", 0.5f, null, ""), "motor", Catalog);

        Assert.That(fused.Source, Is.EqualTo(AnswerSource.Vision));
        Assert.That(fused.Help, Is.EqualTo("Opens above 6 bar to protect the line."), "no help from the model → the twin's description");
    }

    [Test]
    public void A_failed_request_falls_back_to_the_gazed_part()
    {
        var fused = AnswerFusion.Fuse(null, "relief_valve", Catalog);

        Assert.That(fused.Source, Is.EqualTo(AnswerSource.ModelData));
        Assert.That(fused.PartId, Is.EqualTo("relief_valve"));
        Assert.That(fused.Confidence, Is.Zero);
    }

    [Test]
    public void A_part_id_the_twin_does_not_know_is_dropped_but_the_words_kept()
    {
        var fused = AnswerFusion.Fuse(new VisionAnswer("flow meter", "flow_meter", 0.8f, Box, ""), null, Catalog);

        Assert.That(fused.Source, Is.EqualTo(AnswerSource.Vision));
        Assert.That(fused.PartId, Is.Null);
        Assert.That(fused.Label, Is.EqualTo("flow meter"));
        StringAssert.Contains("isn't part of the machine", fused.Describe());
    }

    [Test]
    public void A_confident_answer_about_something_off_the_machine_is_kept()
    {
        var fused = AnswerFusion.Fuse(new VisionAnswer("a torque wrench", null, 0.85f, Box, "Set it to 25 Nm for the flange bolts."), "motor", Catalog);

        Assert.That(fused.Source, Is.EqualTo(AnswerSource.Vision), "the user may be holding a tool in front of the motor");
        Assert.That(fused.Label, Is.EqualTo("a torque wrench"));
    }

    [Test]
    public void An_unsure_answer_with_nothing_under_the_gaze_is_reported_as_unsure()
    {
        var fused = AnswerFusion.Fuse(new VisionAnswer("a hose", null, 0.2f, Box, ""), null, Catalog);

        Assert.That(fused.Source, Is.EqualTo(AnswerSource.Vision));
        Assert.That(fused.IsUnsure, Is.True);
        StringAssert.StartsWith("Not sure", fused.Describe());
    }

    [Test]
    public void Nothing_to_say_without_an_answer_or_a_gazed_part()
    {
        Assert.That(AnswerFusion.Fuse(null, null, Catalog).Source, Is.EqualTo(AnswerSource.None));
        Assert.That(AnswerFusion.Fuse(new VisionAnswer("", null, 0.9f, null, ""), "not_a_part", Catalog).Source, Is.EqualTo(AnswerSource.None));
        StringAssert.Contains("ask again", FusedAnswer.Nothing.Describe());
    }

    [Test]
    public void Boxes_outside_the_image_or_empty_are_ignored()
    {
        Assert.That(AnswerFusion.Fuse(new VisionAnswer("motor", "motor", 0.9f, new NormalizedBox(0.2f, 0.2f, 1.4f, 0.5f), ""), null, Catalog).Box, Is.Null);
        Assert.That(AnswerFusion.Fuse(new VisionAnswer("motor", "motor", 0.9f, new NormalizedBox(0.5f, 0.5f, 0.5f, 0.6f), ""), null, Catalog).Box, Is.Null);
        Assert.That(new NormalizedBox(0.6f, 0.2f, 0.4f, 0.4f).IsValid, Is.False, "reversed corners");
        Assert.That(Box.Center.x, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(Box.Center.y, Is.EqualTo(0.4f).Within(1e-6f));
    }

    [Test]
    public void Confidence_is_clamped_and_blank_ids_mean_none()
    {
        Assert.That(new VisionAnswer("x", " ", 7f, null, null).Confidence, Is.EqualTo(1f));
        Assert.That(new VisionAnswer("x", " ", float.NaN, null, null).Confidence, Is.Zero);
        Assert.That(new VisionAnswer("x", " ", 0.5f, null, null).PartId, Is.Null);
    }
}
