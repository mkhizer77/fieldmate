using Fieldmate.AI;
using Fieldmate.Procedures;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>#71: the mate's setup lines.</summary>
public class SetupScriptTests
{
    [Test]
    public void Intro_names_the_mate_and_says_how_to_place_it()
    {
        var line = SetupScript.Intro("point your right hand where you want me and pinch");
        Assert.That(line, Does.StartWith("Hi, I'm Fieldmate"));
        Assert.That(line, Does.EndWith("point your right hand where you want me and pinch."));
    }

    [Test]
    public void Machine_line_capitalises_the_input_hint()
    {
        Assert.That(SetupScript.PlaceMachine("point at an open spot on the floor and pinch"),
            Does.Contain("Point at an open spot on the floor and pinch to place it"));
    }

    [Test]
    public void Briefing_comes_from_the_procedure_sounds_human_and_ends_with_the_first_task()
    {
        var line = SetupScript.Briefing(DemoProcedures.ReliefValveReplacement(), "Have a good look at the relief valve first.");
        Assert.That(line, Does.StartWith("Today we're going to replace the relief valve cartridge. It's ten steps"));
        Assert.That(line, Does.EndWith("Let's start. Have a good look at the relief valve first."));
        Assert.That(line, Does.Not.Contain("Step 1"), "no status-readout numbering");
        Assert.That(SetupScript.Briefing(DemoProcedures.ReliefValveReplacement(), null), Does.EndWith("First, inspect the relief valve."));
    }

    [Test]
    public void Captions_stay_long_enough_to_read()
    {
        Assert.That(SetupScript.ReadSeconds("Hi."), Is.EqualTo(2.5f));
        Assert.That(SetupScript.ReadSeconds(new string('a', 75)), Is.EqualTo(5f).Within(1e-4f));
        Assert.That(SetupScript.ReadSeconds(new string('a', 600)), Is.EqualTo(9f));
    }
}
