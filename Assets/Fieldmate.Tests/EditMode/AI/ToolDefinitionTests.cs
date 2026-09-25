using System;
using System.Linq;
using Fieldmate.AI;
using Fieldmate.Json;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

public class ToolDefinitionTests
{
    [Test]
    public void V1_HasTheEightDesignTools()
    {
        Assert.That(FieldmateTools.CreateV1().Select(t => t.Name), Is.EqualTo(new[]
        {
            "highlight_part", "start_procedure", "go_to_step", "show_manual",
            "read_telemetry", "log_note", "set_language", "identify_view",
        }));
    }

    [Test]
    public void InputSchema_IsStrictJsonSchema()
    {
        var goToStep = FieldmateTools.CreateV1().Single(t => t.Name == FieldmateTools.GoToStep);

        Assert.That(goToStep.ToInputSchema().ToJson(), Is.EqualTo(
            "{\"type\":\"object\",\"properties\":{\"index\":{\"type\":\"integer\",\"description\":\"1-based step number.\"," +
            "\"minimum\":1,\"maximum\":20}},\"required\":[\"index\"],\"additionalProperties\":false}"));
    }

    [Test]
    public void InputSchema_CoversEnumsOptionalAndMaxLength()
    {
        var tools = FieldmateTools.CreateV1().ToDictionary(t => t.Name);

        var language = tools[FieldmateTools.SetLanguage].ToInputSchema()["properties"]["language"];
        Assert.That(language["enum"].AsStringList(), Is.EqualTo(new[] { "de", "en" }));

        var telemetry = tools[FieldmateTools.ReadTelemetry].ToInputSchema();
        Assert.That(telemetry["required"].Items, Is.Empty);
        Assert.That(telemetry["properties"].Has("part_id"), Is.True);

        Assert.That(tools[FieldmateTools.LogNote].ToInputSchema()["properties"]["text"]["maxLength"].AsNumber(), Is.EqualTo(500));
        Assert.That(tools[FieldmateTools.IdentifyView].ToInputSchema()["properties"].Members, Is.Empty);
    }

    [Test]
    public void AllSchemas_AreParseableJson_AndDescribed()
    {
        foreach (var tool in FieldmateTools.CreateV1())
        {
            Assert.That(JsonReader.Parse(tool.ToInputSchema().ToJson())["type"].AsString(), Is.EqualTo("object"), tool.Name);
            Assert.That(tool.Description.Length, Is.GreaterThan(20), tool.Name);
            Assert.That(tool.Parameters.All(p => p.Description.Length > 0), Is.True, tool.Name);
        }
    }

    [TestCase("highlight_part", true)]
    [TestCase("a1_b2", true)]
    [TestCase("", false)]
    [TestCase(null, false)]
    [TestCase("HighlightPart", false)]
    [TestCase("1tool", false)]
    [TestCase("has-dash", false)]
    [TestCase("has space", false)]
    public void IsValidName_EnforcesSnakeCase(string name, bool valid)
    {
        Assert.That(ToolDefinition.IsValidName(name), Is.EqualTo(valid));
        Assert.That(ToolDefinition.IsValidName(new string('a', 65)), Is.False);
    }

    [Test]
    public void Constructors_RejectInvalidDefinitions()
    {
        var p = new ToolParameter("p", ToolParameterType.String, "d");

        Assert.Throws<ArgumentException>(() => new ToolDefinition("Bad", "desc"));
        Assert.Throws<ArgumentException>(() => new ToolDefinition("ok", " "));
        Assert.Throws<ArgumentException>(() => new ToolDefinition("ok", "desc", p, p));
        Assert.Throws<ArgumentException>(() => new ToolDefinition("ok", "desc", (ToolParameter)null));
        Assert.Throws<ArgumentException>(() => new ToolParameter("Bad Name", ToolParameterType.String, "d"));
        Assert.Throws<ArgumentException>(() => new ToolParameter("n", ToolParameterType.Integer, "d", allowedValues: new[] { "1" }));
    }

    [Test]
    public void TryGetParameter_FindsByName()
    {
        var tool = FieldmateTools.CreateV1()[0];

        Assert.That(tool.TryGetParameter("part_id", out var parameter) && parameter.Required, Is.True);
        Assert.That(tool.TryGetParameter("nope", out var none), Is.False);
        Assert.That(none, Is.Null);
    }
}
