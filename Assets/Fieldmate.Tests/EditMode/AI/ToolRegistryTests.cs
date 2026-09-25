using System;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

public class ToolRegistryTests
{
    private ToolRegistry registry;

    [SetUp]
    public void SetUp() => registry = FieldmateTools.CreateRegistry();

    private static ToolCall Call(string name, string args) => new("call_1", name, args);

    private string ErrorFor(string name, string args)
    {
        Assert.That(registry.TryValidate(Call(name, args), out var parsed, out var error), Is.False, $"{name}({args}) should be rejected");
        Assert.That(parsed, Is.Null);
        return error;
    }

    [TestCase(FieldmateTools.HighlightPart, "{\"part_id\":\"relief_valve\"}")]
    [TestCase(FieldmateTools.StartProcedure, "{\"procedure_id\":\"relief_valve_replacement\"}")]
    [TestCase(FieldmateTools.GoToStep, "{\"index\":3}")]
    [TestCase(FieldmateTools.GoToStep, "{\"index\":3.0}")]
    [TestCase(FieldmateTools.ShowManual, "{\"section_id\":\"safety.loto\"}")]
    [TestCase(FieldmateTools.ReadTelemetry, "{}")]
    [TestCase(FieldmateTools.ReadTelemetry, "{\"part_id\":null}")]
    [TestCase(FieldmateTools.ReadTelemetry, "{\"part_id\":\"motor\"}")]
    [TestCase(FieldmateTools.LogNote, "{\"text\":\"Cartridge was corroded.\"}")]
    [TestCase(FieldmateTools.SetLanguage, "{\"language\":\"de\"}")]
    [TestCase(FieldmateTools.IdentifyView, "")]
    [TestCase(FieldmateTools.IdentifyView, "{}")]
    public void ValidCalls_Pass(string name, string args)
    {
        Assert.That(registry.TryValidate(Call(name, args), out var parsed, out var error), Is.True, error);
        Assert.That(parsed, Is.Not.Null);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void UnknownTool_ListsAvailableTools()
    {
        var error = ErrorFor("delete_everything", "{}");

        Assert.That(error, Does.StartWith("Unknown tool 'delete_everything'. Available tools: highlight_part, start_procedure"));
    }

    [Test]
    public void MalformedJson_IsExplained()
    {
        Assert.That(ErrorFor(FieldmateTools.GoToStep, "{index: 3}"), Does.StartWith("Arguments for 'go_to_step' are not valid JSON (").And.EndWith("Send one JSON object."));
        Assert.That(ErrorFor(FieldmateTools.GoToStep, "[3]"), Is.EqualTo("Arguments for 'go_to_step' must be a JSON object, not an array."));
    }

    [Test]
    public void MissingAndUnexpectedArguments_AreNamed()
    {
        Assert.That(ErrorFor(FieldmateTools.HighlightPart, "{}"), Is.EqualTo("Missing required argument 'part_id' (string) for 'highlight_part'."));
        Assert.That(ErrorFor(FieldmateTools.HighlightPart, "{\"part_id\":null}"), Does.StartWith("Missing required argument 'part_id'"));
        Assert.That(ErrorFor(FieldmateTools.HighlightPart, "{\"part_id\":\"x\",\"colour\":\"red\"}"),
            Is.EqualTo("Unexpected argument 'colour' for 'highlight_part'. Allowed: part_id."));
        Assert.That(ErrorFor(FieldmateTools.IdentifyView, "{\"zoom\":2}"), Does.EndWith("Allowed: no arguments."));
    }

    [Test]
    public void TypeRangeEnumAndLengthViolations_AreExplained()
    {
        Assert.That(ErrorFor(FieldmateTools.GoToStep, "{\"index\":\"3\"}"), Is.EqualTo("Argument 'index' of 'go_to_step' must be an integer, got a string."));
        Assert.That(ErrorFor(FieldmateTools.GoToStep, "{\"index\":2.5}"), Is.EqualTo("Argument 'index' of 'go_to_step' must be a whole number, got 2.5."));
        Assert.That(ErrorFor(FieldmateTools.GoToStep, "{\"index\":0}"), Is.EqualTo("Argument 'index' of 'go_to_step' must be between 1 and 20, got 0."));
        Assert.That(ErrorFor(FieldmateTools.SetLanguage, "{\"language\":\"fr\"}"), Is.EqualTo("Argument 'language' of 'set_language' must be one of: de, en. Got 'fr'."));
        Assert.That(ErrorFor(FieldmateTools.HighlightPart, "{\"part_id\":7}"), Is.EqualTo("Argument 'part_id' of 'highlight_part' must be a string, got a number."));
        Assert.That(ErrorFor(FieldmateTools.HighlightPart, "{\"part_id\":\"  \"}"), Is.EqualTo("Argument 'part_id' of 'highlight_part' must not be empty."));
        Assert.That(ErrorFor(FieldmateTools.LogNote, $"{{\"text\":\"{new string('a', 501)}\"}}"), Does.Contain("at most 500 characters, got 501"));
    }

    [Test]
    public void BooleanAndNumberParameters_AreChecked()
    {
        registry.Register(new ToolDefinition("set_volume", "Set volume.",
            new ToolParameter("level", ToolParameterType.Number, "0-1", minimum: 0, maximum: 1),
            new ToolParameter("muted", ToolParameterType.Boolean, "Mute", required: false)));

        Assert.That(registry.TryValidate(Call("set_volume", "{\"level\":0.5,\"muted\":true}"), out var args, out _), Is.True);
        Assert.That(args.GetNumber("level"), Is.EqualTo(0.5));
        Assert.That(args.GetBool("muted"), Is.True);
        Assert.That(ErrorFor("set_volume", "{\"level\":1.5}"), Does.Contain("between 0 and 1, got 1.5"));
        Assert.That(ErrorFor("set_volume", "{\"level\":true}"), Does.Contain("must be a number, got a boolean"));
        Assert.That(ErrorFor("set_volume", "{\"level\":0,\"muted\":\"yes\"}"), Does.Contain("must be true or false, got a string"));
    }

    [Test]
    public void ToolArguments_ReadValuesWithFallbacks()
    {
        registry.TryValidate(Call(FieldmateTools.GoToStep, "{\"index\":4}"), out var args, out _);

        Assert.That(args.GetInt("index"), Is.EqualTo(4));
        Assert.That(args.Has("index"), Is.True);
        Assert.That(args.GetString("missing", "x"), Is.EqualTo("x"));
        Assert.That(args.GetInt("missing", 9), Is.EqualTo(9));
        Assert.That(new ToolArguments(null).Has("a"), Is.False);
    }

    [Test]
    public async Task ExecuteAsync_RunsExecutor_WithValidatedArguments()
    {
        var executor = new FakeExecutor((call, args) => ToolResult.Success(call, $"Highlighted {args.GetString("part_id")}."));
        registry.SetExecutor(FieldmateTools.HighlightPart, executor);

        var result = await registry.ExecuteAsync(Call(FieldmateTools.HighlightPart, "{\"part_id\":\"motor\"}"), CancellationToken.None);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content, Is.EqualTo("Highlighted motor."));
        Assert.That(result.ToolCallId, Is.EqualTo("call_1"));
        Assert.That(result.ToolName, Is.EqualTo(FieldmateTools.HighlightPart));
        Assert.That(executor.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_InvalidCall_NeverReachesExecutor()
    {
        var executor = new FakeExecutor((call, _) => ToolResult.Success(call, "ran"));
        registry.SetExecutor(FieldmateTools.GoToStep, executor);

        var result = await registry.ExecuteAsync(Call(FieldmateTools.GoToStep, "{\"index\":-1}"), CancellationToken.None);

        Assert.That(result.IsError, Is.True);
        Assert.That(executor.Calls, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_ReportsMissingExecutorNullResultAndExceptions()
    {
        var missing = await registry.ExecuteAsync(Call(FieldmateTools.IdentifyView, "{}"), CancellationToken.None);
        Assert.That(missing.Content, Is.EqualTo("Tool 'identify_view' is not available right now."));

        registry.SetExecutor(FieldmateTools.IdentifyView, new FakeExecutor((_, _) => null));
        var empty = await registry.ExecuteAsync(Call(FieldmateTools.IdentifyView, "{}"), CancellationToken.None);
        Assert.That(empty.Content, Does.Contain("returned no result"));

        registry.SetExecutor(FieldmateTools.IdentifyView, new FakeExecutor((_, _) => throw new InvalidOperationException("camera busy")));
        var failed = await registry.ExecuteAsync(Call(FieldmateTools.IdentifyView, "{}"), CancellationToken.None);
        Assert.That(failed.IsError, Is.True);
        Assert.That(failed.Content, Is.EqualTo("Tool 'identify_view' failed: camera busy"));
    }

    [Test]
    public void ExecuteAsync_PropagatesCancellation()
    {
        registry.SetExecutor(FieldmateTools.IdentifyView, new FakeExecutor((_, _) => throw new OperationCanceledException()));

        Assert.CatchAsync<OperationCanceledException>(() => registry.ExecuteAsync(Call(FieldmateTools.IdentifyView, "{}"), CancellationToken.None));
    }

    [Test]
    public void Registration_RejectsDuplicatesAndUnknownExecutors()
    {
        Assert.Throws<ArgumentException>(() => registry.Register(FieldmateTools.CreateV1()[0]));
        Assert.Throws<ArgumentNullException>(() => registry.Register(null));
        Assert.Throws<ArgumentException>(() => registry.SetExecutor("nope", new FakeExecutor((c, _) => ToolResult.Success(c, ""))));
        Assert.Throws<ArgumentNullException>(() => registry.TryValidate(null, out _, out _));
    }

    [Test]
    public void SetExecutor_NullRemovesIt()
    {
        registry.Register(new ToolDefinition("ping", "Ping."), new FakeExecutor((c, _) => ToolResult.Success(c, "pong")));
        Assert.That(registry.HasExecutor("ping"), Is.True);

        registry.SetExecutor("ping", null);
        Assert.That(registry.HasExecutor("ping"), Is.False);
        Assert.That(registry.HasExecutor(null), Is.False);
    }

    [Test]
    public void EmptyRegistry_SaysNoToolsAvailable()
    {
        new ToolRegistry().TryValidate(Call("x", "{}"), out _, out var error);

        Assert.That(error, Is.EqualTo("Unknown tool 'x'. Available tools: none."));
    }

    [Test]
    public void ToolResult_ConvertsToToolMessage()
    {
        var message = ToolResult.Failure(Call("x", "{}"), "nope").ToMessage();

        Assert.That((message.Role, message.ToolCallId, message.Text, message.IsError), Is.EqualTo((ChatRole.Tool, "call_1", "nope", true)));
    }

    private sealed class FakeExecutor : IToolExecutor
    {
        private readonly Func<ToolCall, ToolArguments, ToolResult> run;

        public FakeExecutor(Func<ToolCall, ToolArguments, ToolResult> run) => this.run = run;

        public int Calls { get; private set; }

        public Task<ToolResult> ExecuteAsync(ToolCall call, ToolArguments arguments, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(run(call, arguments));
        }
    }
}
