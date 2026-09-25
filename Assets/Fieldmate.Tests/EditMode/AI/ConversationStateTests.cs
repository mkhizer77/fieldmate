using System;
using System.Linq;
using Fieldmate.AI;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

public class ConversationStateTests
{
    private static ChatResponse Say(string text) => new(text, null, StopReason.EndTurn);

    private static ChatResponse CallTool(string id, string name = "highlight_part", string args = "{\"part_id\":\"motor\"}") =>
        new(string.Empty, new[] { new ToolCall(id, name, args) }, StopReason.ToolUse);

    private static ToolResult Result(string id, string content = "ok") =>
        ToolResult.Success(new ToolCall(id, "highlight_part", "{}"), content);

    [Test]
    public void ToolTurn_RecordsCallsAndResultsInOrder()
    {
        var conversation = new ConversationState();
        conversation.AddUser("Where is the motor?");
        conversation.AddAssistant(CallTool("c1"));

        Assert.That(conversation.PendingToolCalls.Select(c => c.Id), Is.EqualTo(new[] { "c1" }));

        conversation.AddToolResult(Result("c1", "Highlighted the drive motor."));
        conversation.AddAssistant(Say("It's the grey unit on the left [part.motor]."));

        Assert.That(conversation.PendingToolCalls, Is.Empty);
        Assert.That(conversation.Messages.Select(m => m.Role), Is.EqualTo(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant }));
        Assert.That(conversation.Messages[2].ToolCallId, Is.EqualTo("c1"));
    }

    [Test]
    public void TrimsWholeTurns_ByMessageCount()
    {
        var conversation = new ConversationState(maxMessages: 4);
        for (var i = 1; i <= 3; i++)
        {
            conversation.AddUser($"question {i}");
            conversation.AddAssistant(Say($"answer {i}"));
        }

        Assert.That(conversation.Messages.Select(m => m.Text), Is.EqualTo(new[] { "question 2", "answer 2", "question 3", "answer 3" }));
    }

    [Test]
    public void TrimsWholeTurns_ByCharacters_NeverSplittingToolPairs()
    {
        var conversation = new ConversationState(maxMessages: 50, maxCharacters: 120);
        conversation.AddUser("Highlight the motor please.");
        conversation.AddAssistant(CallTool("c1"));
        conversation.AddToolResult(Result("c1", "Highlighted the drive motor."));
        conversation.AddAssistant(Say("Done."));
        conversation.AddUser(new string('x', 60));
        conversation.AddAssistant(Say(new string('y', 40)));

        Assert.That(conversation.Messages.First().Role, Is.EqualTo(ChatRole.User));
        Assert.That(conversation.Messages.Any(m => m.Role == ChatRole.Tool), Is.False, "the whole tool turn was dropped together");
        Assert.That(conversation.Characters, Is.LessThanOrEqualTo(120));
    }

    [Test]
    public void KeepsTheLatestTurn_EvenOverBudget()
    {
        var conversation = new ConversationState(maxMessages: 2, maxCharacters: 10);
        conversation.AddUser("first");
        conversation.AddUser(new string('z', 50));

        Assert.That(conversation.Messages.Single().Text, Has.Length.EqualTo(50));
    }

    [Test]
    public void Characters_CountTextAndToolArguments()
    {
        var conversation = new ConversationState();
        conversation.AddUser("abc");
        conversation.AddAssistant(CallTool("c1", "go_to_step", "{\"index\":2}"));

        Assert.That(conversation.Characters, Is.EqualTo(3 + "go_to_step".Length + "{\"index\":2}".Length));
    }

    [Test]
    public void EnforcesTurnOrder()
    {
        var conversation = new ConversationState();

        Assert.Throws<InvalidOperationException>(() => conversation.AddAssistant(Say("hi")), "must start with a user message");

        conversation.AddUser("go");
        conversation.AddAssistant(CallTool("c1"));
        Assert.Throws<InvalidOperationException>(() => conversation.AddUser("next"), "pending tool call");
        Assert.Throws<InvalidOperationException>(() => conversation.AddAssistant(Say("x")), "pending tool call");
        Assert.Throws<InvalidOperationException>(() => conversation.AddToolResult(Result("other")), "unknown call id");

        conversation.AddToolResult(Result("c1"));
        Assert.Throws<InvalidOperationException>(() => conversation.AddToolResult(Result("c1")), "already answered");
    }

    [Test]
    public void RejectsInvalidInput()
    {
        var conversation = new ConversationState();

        Assert.Throws<ArgumentException>(() => conversation.AddUser(" "));
        Assert.Throws<ArgumentNullException>(() => conversation.AddAssistant(null));
        Assert.Throws<ArgumentNullException>(() => conversation.AddToolResult(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversationState(maxMessages: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversationState(maxCharacters: 0));
    }

    [Test]
    public void Clear_EmptiesTheConversation()
    {
        var conversation = new ConversationState();
        conversation.AddUser("hi");
        conversation.Clear();

        Assert.That(conversation.Messages, Is.Empty);
        Assert.That(conversation.Characters, Is.Zero);
    }
}
