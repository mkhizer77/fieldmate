using System;
using System.Linq;
using Fieldmate.AI;
using Fieldmate.Json;
using Fieldmate.Providers;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Providers;

public class AnthropicWireTests
{
    private static ChatRequest Request(ChatMessage[] messages, string context = null) =>
        new("You are Fieldmate.", messages, FieldmateTools.CreateV1().Take(1).ToArray(), context, 300);

    [Test]
    public void ToRequest_MapsSystemToolsAndCap()
    {
        var json = AnthropicWire.ToRequest(Request(new[] { ChatMessage.User("Hi") }));

        Assert.That(json["system"].AsString(), Is.EqualTo("You are Fieldmate."));
        Assert.That(json["max_tokens"].AsNumber(), Is.EqualTo(300));
        Assert.That(json["tools"][0]["name"].AsString(), Is.EqualTo("highlight_part"));
        Assert.That(json["tools"][0]["input_schema"]["additionalProperties"].AsBoolean(true), Is.False);
        Assert.That(json["messages"][0].ToJson(), Is.EqualTo("{\"role\":\"user\",\"content\":\"Hi\"}"));
    }

    [Test]
    public void Context_GoesIntoTheCurrentTurnsUserMessage_NotTheSystemPrompt()
    {
        var messages = new[]
        {
            ChatMessage.User("earlier question"),
            ChatMessage.Assistant("earlier answer"),
            ChatMessage.User("Why is the pressure high?"),
            ChatMessage.Assistant("", new[] { new ToolCall("t1", "read_telemetry", "{}") }),
            ChatMessage.ToolResult("t1", "pressure 6.8 bar", false),
        };

        var json = AnthropicWire.ToRequest(Request(messages, "Current step: none."));
        var sent = json["messages"];

        Assert.That(json["system"].AsString(), Is.EqualTo("You are Fieldmate."), "system stays stable for caching");
        Assert.That(sent[0]["content"].AsString(), Is.EqualTo("earlier question"));
        Assert.That(sent[2]["content"][0]["text"].AsString(), Is.EqualTo("<context>\nCurrent step: none.\n</context>"));
        Assert.That(sent[2]["content"][1]["text"].AsString(), Is.EqualTo("Why is the pressure high?"));
    }

    [Test]
    public void ToolCallsAndResults_UseAnthropicBlocks_ResultsGroupedInOneUserMessage()
    {
        var messages = new[]
        {
            ChatMessage.User("Highlight the motor and read the telemetry."),
            ChatMessage.Assistant("Sure.", new[]
            {
                new ToolCall("t1", "highlight_part", "{\"part_id\":\"motor\"}"),
                new ToolCall("t2", "read_telemetry", "not json"),
            }),
            ChatMessage.ToolResult("t1", "Highlighted.", false),
            ChatMessage.ToolResult("t2", "Sensor offline.", true),
        };

        var sent = AnthropicWire.ToRequest(Request(messages))["messages"];

        Assert.That(sent.Items, Has.Count.EqualTo(3));
        var assistant = sent[1]["content"];
        Assert.That(assistant[0].ToJson(), Is.EqualTo("{\"type\":\"text\",\"text\":\"Sure.\"}"));
        Assert.That(assistant[1].ToJson(), Is.EqualTo("{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"highlight_part\",\"input\":{\"part_id\":\"motor\"}}"));
        Assert.That(assistant[2]["input"].ToJson(), Is.EqualTo("{}"), "unparseable arguments become an empty object");

        var results = sent[2];
        Assert.That(results["role"].AsString(), Is.EqualTo("user"));
        Assert.That(results["content"].Items, Has.Count.EqualTo(2));
        Assert.That(results["content"][1].ToJson(), Is.EqualTo("{\"type\":\"tool_result\",\"tool_use_id\":\"t2\",\"content\":\"Sensor offline.\",\"is_error\":true}"));
    }

    [Test]
    public void ToRequest_OmitsEmptySystemAndTools()
    {
        var json = AnthropicWire.ToRequest(new ChatRequest("", new[] { ChatMessage.User("x") }, null));

        Assert.That(json.Has("system"), Is.False);
        Assert.That(json.Has("tools"), Is.False);
    }

    [Test]
    public void FromResponse_ReadsTextToolCallsAndStopReason()
    {
        var response = AnthropicWire.FromResponse(JsonReader.Parse(
            "{\"content\":[{\"type\":\"text\",\"text\":\"Checking. \"},{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"read_telemetry\",\"input\":{\"part_id\":\"motor\"}},{\"type\":\"text\",\"text\":\"One moment.\"}],\"stop_reason\":\"tool_use\"}"));

        Assert.That(response.Text, Is.EqualTo("Checking. One moment."));
        Assert.That(response.StopReason, Is.EqualTo(StopReason.ToolUse));
        Assert.That(response.ToolCalls.Single().Id, Is.EqualTo("toolu_1"));
        Assert.That(response.ToolCalls.Single().ArgumentsJson, Is.EqualTo("{\"part_id\":\"motor\"}"));
    }

    [TestCase("end_turn", StopReason.EndTurn)]
    [TestCase("max_tokens", StopReason.MaxTokens)]
    [TestCase("refusal", StopReason.Other)]
    public void FromResponse_MapsStopReasons(string wire, StopReason expected)
    {
        var json = JsonReader.Parse($"{{\"content\":[],\"stop_reason\":\"{wire}\"}}");

        Assert.That(AnthropicWire.FromResponse(json).StopReason, Is.EqualTo(expected));
    }

    [Test]
    public void FromResponse_RejectsResponsesWithoutContent()
    {
        var e = Assert.Throws<ProviderException>(() => AnthropicWire.FromResponse(JsonReader.Parse("{\"stop_reason\":\"end_turn\"}")));
        Assert.That(e.ErrorType, Is.EqualTo(ProviderException.InvalidResponse));
    }
}
