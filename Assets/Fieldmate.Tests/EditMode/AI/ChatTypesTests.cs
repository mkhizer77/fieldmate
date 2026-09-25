using System;
using Fieldmate.AI;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

public class ChatTypesTests
{
    [Test]
    public void Messages_HaveRoleSpecificFields()
    {
        var user = ChatMessage.User(null);
        var assistant = ChatMessage.Assistant("hi");
        var tool = ChatMessage.ToolResult("c1", "done", false);

        Assert.That((user.Role, user.Text, user.ToolCalls.Count), Is.EqualTo((ChatRole.User, "", 0)));
        Assert.That(assistant.Role, Is.EqualTo(ChatRole.Assistant));
        Assert.That((tool.Role, tool.ToolCallId, tool.IsError), Is.EqualTo((ChatRole.Tool, "c1", false)));
        Assert.Throws<ArgumentException>(() => ChatMessage.ToolResult("", "x", false));
    }

    [Test]
    public void ToolCall_RequiresId()
    {
        Assert.Throws<ArgumentException>(() => new ToolCall(null, "x", "{}"));
        Assert.That(new ToolCall("1", null, "{}").Name, Is.Empty);
        Assert.That(new ToolCall("1", "go_to_step", "{\"index\":2}").ToString(), Is.EqualTo("go_to_step({\"index\":2})"));
    }

    [Test]
    public void Request_AndResponse_DefaultOptionalParts()
    {
        var request = new ChatRequest(null, Array.Empty<ChatMessage>(), null);
        var response = new ChatResponse(null, null, StopReason.EndTurn);

        Assert.That(request.SystemPrompt, Is.Empty);
        Assert.That(request.Tools, Is.Empty);
        Assert.That(request.Context, Is.Empty);
        Assert.That(request.MaxOutputTokens, Is.EqualTo(400));
        Assert.That(response.Text, Is.Empty);
        Assert.That(response.ToolCalls, Is.Empty);
        Assert.Throws<ArgumentNullException>(() => new ChatRequest("s", null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRequest("s", Array.Empty<ChatMessage>(), null, maxOutputTokens: 0));
    }

    [Test]
    public void AudioData_AndTranscript()
    {
        var audio = new AudioData(new float[16000], 16000);
        Assert.That(audio.DurationSeconds, Is.EqualTo(1d));
        Assert.Throws<ArgumentNullException>(() => new AudioData(null, 16000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioData(new float[1], 0));

        Assert.That(new Transcript("  ", "en").IsEmpty, Is.True);
        Assert.That(new Transcript(null, null).LanguageCode, Is.Empty);
        Assert.That(new Transcript("hello", "en", 0.8f).Confidence, Is.EqualTo(0.8f));
    }
}
