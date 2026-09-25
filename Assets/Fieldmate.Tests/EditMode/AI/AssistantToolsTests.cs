using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.AI;

public class AssistantToolsTests
{
    private MachineManual manual;
    private ProcedureRunner runner;
    private TelemetryModel telemetry;
    private FakeScene scene;
    private AssistantTools tools;
    private ToolRegistry registry;

    [SetUp]
    public void SetUp()
    {
        manual = MachineManual.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Manual", "manual.json")));
        runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        telemetry = new TelemetryModel(FaultModel.CreateDefault());
        scene = new FakeScene();
        tools = new AssistantTools(manual, runner, telemetry, scene);
        registry = FieldmateTools.CreateRegistry();
        tools.AttachTo(registry);
    }

    private Task<ToolResult> Run(string tool, string args) => registry.ExecuteAsync(new ToolCall("c1", tool, args), CancellationToken.None);

    [Test]
    public void EveryV1Tool_HasAnExecutor()
    {
        Assert.That(registry.Definitions.All(d => registry.HasExecutor(d.Name)), Is.True);
    }

    [Test]
    public async Task HighlightPart_PlacedUnplacedAndUnknown()
    {
        var ok = await Run("highlight_part", "{\"part_id\":\"relief_valve\"}");
        var unplaced = await Run("highlight_part", "{\"part_id\":\"pump_cover\"}");
        var unknown = await Run("highlight_part", "{\"part_id\":\"flux_capacitor\"}");

        Assert.That((ok.IsError, ok.Content), Is.EqualTo((false, "Highlighted the Pressure relief valve for the user.")));
        Assert.That(scene.Highlighted, Is.EqualTo(new[] { "relief_valve" }));
        Assert.That(unplaced.Content, Is.EqualTo("The Pump access cover is not placed in the scene yet."));
        Assert.That(unknown.IsError, Is.True);
        Assert.That(unknown.Content, Does.StartWith("Unknown part 'flux_capacitor'. Known parts: electrical_cabinet, main_breaker"));
    }

    [Test]
    public async Task StartProcedure_StartsOnceAndShowsStepOne()
    {
        var started = await Run("start_procedure", "{\"procedure_id\":\"relief_valve_replacement\"}");
        var again = await Run("start_procedure", "{\"procedure_id\":\"relief_valve_replacement\"}");
        var unknown = await Run("start_procedure", "{\"procedure_id\":\"make_coffee\"}");

        Assert.That(started.Content, Is.EqualTo("Started 'Replace the relief valve cartridge'. Step 1 of 7: Inspect the relief valve."));
        Assert.That(runner.State, Is.EqualTo(RunnerState.Running));
        Assert.That(scene.ShownStep, Is.EqualTo(1));
        Assert.That(again.Content, Does.Contain("already running at step 1"));
        Assert.That(unknown.Content, Does.Contain("Available: relief_valve_replacement"));
    }

    [Test]
    public async Task GoToStep_ShowsWithoutAdvancing()
    {
        Assert.That((await Run("go_to_step", "{\"index\":2}")).Content, Is.EqualTo("No procedure is running. Start one first."));

        await Run("start_procedure", "{\"procedure_id\":\"relief_valve_replacement\"}");
        var shown = await Run("go_to_step", "{\"index\":3}");
        var outOfRange = await Run("go_to_step", "{\"index\":9}");

        Assert.That(shown.Content, Is.EqualTo("Showing step 3 of 7: Close the inlet valve. The current step is still 1."));
        Assert.That(scene.ShownStep, Is.EqualTo(3));
        Assert.That(runner.CurrentStepIndex, Is.Zero, "showing a step never completes one");
        Assert.That(outOfRange.Content, Is.EqualTo("'Replace the relief valve cartridge' has steps 1 to 7."));
    }

    [Test]
    public async Task ShowManual_KnownAndUnknownSections()
    {
        var shown = await Run("show_manual", "{\"section_id\":\"safety.loto\"}");
        var unknown = await Run("show_manual", "{\"section_id\":\"chapter.9\"}");

        Assert.That(shown.Content, Is.EqualTo("Showing [safety.loto] Lockout / tagout on the panel."));
        Assert.That(scene.Shown.Id, Is.EqualTo("safety.loto"));
        Assert.That(unknown.IsError, Is.True);
    }

    [Test]
    public async Task ReadTelemetry_ReportsAllChannelsWithStatus()
    {
        telemetry.Faults.Inject(FaultModel.Overpressure);
        for (var i = 0; i < 100; i++) telemetry.Step(0.1f);

        var reading = await Run("read_telemetry", "{\"part_id\":\"pressure_gauge\"}");
        var unknown = await Run("read_telemetry", "{\"part_id\":\"warp_core\"}");

        Assert.That(reading.Content, Does.StartWith("pressure 6.8 bar (ALARM), temperature"));
        Assert.That(reading.Content, Does.Contain("vibration 2.0 mm/s (normal)"));
        Assert.That(unknown.IsError, Is.True);
        Assert.That((await Run("read_telemetry", "{}")).IsError, Is.False);
    }

    [Test]
    public async Task LogNote_SetLanguage_IdentifyView()
    {
        string language = null;
        tools.LanguageChanged += l => language = l;

        Assert.That((await Run("log_note", "{\"text\":\" Cartridge corroded. \"}")).Content, Is.EqualTo("Noted in the maintenance log."));
        Assert.That(scene.Notes.Single(), Is.EqualTo("Cartridge corroded."));

        Assert.That((await Run("set_language", "{\"language\":\"de\"}")).Content, Is.EqualTo("Language set to German."));
        Assert.That(language, Is.EqualTo("de"));

        Assert.That((await Run("identify_view", "{}")).IsError, Is.True, "vision arrives in M2");
        scene.Identified = "That's the relief valve.";
        Assert.That((await Run("identify_view", "{}")).Content, Is.EqualTo("That's the relief valve."));
    }
}

public class AssistantPromptTests
{
    [Test]
    public void System_IsStablePerLanguage_AndStatesTheRules()
    {
        var en = AssistantPrompt.System("FM-200", "en");

        Assert.That(en, Is.EqualTo(AssistantPrompt.System("FM-200", "en")));
        Assert.That(en, Does.Contain("FM-200").And.Contain("[fault.overpressure]").And.Contain("Reply in English."));
        Assert.That(AssistantPrompt.System("FM-200", "de"), Does.EndWith("Reply in German."));
    }

    [Test]
    public void Context_DescribesProcedureTelemetryGazeAndManual()
    {
        var manual = MachineManual.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Manual", "manual.json")));
        var runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        runner.Start(0);
        manual.TryGetPart("relief_valve", out var gaze);
        var slice = new ManualRetriever(manual).Retrieve(new RetrievalQuery("relief_valve", null, "what is this"));

        var context = AssistantPrompt.Context(runner, new TelemetryModel(FaultModel.CreateDefault()), gaze, slice);

        Assert.That(context, Does.StartWith("Procedure: 'Replace the relief valve cartridge', step 1 of 7: Inspect the relief valve."));
        Assert.That(context, Does.Contain("Telemetry: pressure 4.0 bar (normal)"));
        Assert.That(context, Does.Contain("Looking at: Pressure relief valve (relief_valve)"));
        Assert.That(context, Does.Contain("[part.relief_valve] Pressure relief valve"));
    }

    [Test]
    public void Context_HandlesMissingPieces()
    {
        var context = AssistantPrompt.Context(null, null, null, null);

        Assert.That(context, Is.EqualTo("Procedure: none running.\nLooking at: nothing in particular\nManual sections: none matched this question."));
    }

    [Test]
    public void DescribeProcedure_Completed()
    {
        var runner = new ProcedureRunner(new ProcedureDefinition("p", "Quick", new[] { StepDefinition.Confirm("c", "Confirm") }));
        runner.Start(0);
        runner.Handle(InteractionEvent.Confirmed(1));

        Assert.That(AssistantPrompt.DescribeProcedure(runner), Is.EqualTo("'Quick' completed (score 100)."));
    }

    [TestCase("The valve is stuck [fault.overpressure].", "The valve is stuck.")]
    [TestCase("See [safety.loto] and [proc.rv.lockout] first.", "See and first.")]
    [TestCase("Keep [brackets] that aren't ids.", "Keep [brackets] that aren't ids.")]
    [TestCase(null, "")]
    public void ForSpeech_DropsCitationsOnly(string text, string expected)
    {
        Assert.That(AssistantPrompt.ForSpeech(text), Is.EqualTo(expected));
    }
}
