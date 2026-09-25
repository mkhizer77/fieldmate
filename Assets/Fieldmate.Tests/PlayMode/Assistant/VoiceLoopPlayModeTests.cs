using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>The bench scene with scripted providers: tools must act on the real scene objects.</summary>
public class VoiceLoopPlayModeTests
{
    private VoiceLoop loop;
    private MachineServices machine;
    private AssistantPanel panel;
    private Scripted chat;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null; // let Start run
        loop = UnityEngine.Object.FindAnyObjectByType<VoiceLoop>();
        machine = UnityEngine.Object.FindAnyObjectByType<MachineServices>();
        panel = UnityEngine.Object.FindAnyObjectByType<AssistantPanel>();
        chat = new Scripted();
        loop.Configure(chat, null, null);
    }

    private static IEnumerator Await(Task task)
    {
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted) throw task.Exception!.GetBaseException();
    }

    [UnityTest]
    public IEnumerator Services_LoadManualPartsAndTheDemoFault()
    {
        Assert.That(machine.Manual.Parts, Has.Count.EqualTo(14));
        Assert.That(machine.Parts, Has.Count.EqualTo(14), "every manual part is tagged in the placeholder skid");
        Assert.That(machine.Telemetry.Status(TelemetryChannel.Pressure), Is.EqualTo(ChannelStatus.Alarm), "demo starts with overpressure");
        yield break;
    }

    [UnityTest]
    public IEnumerator HighlightTool_PulsesThePartInTheScene()
    {
        chat.Call("highlight_part", "{\"part_id\":\"relief_valve\"}").Say("There it is [part.relief_valve].");

        yield return Await(loop.AskAsync("Where is the relief valve?", speak: false));

        Assert.That(UnityEngine.Object.FindAnyObjectByType<PartHighlighter>().ActivePartId, Is.EqualTo("relief_valve"));
        Assert.That(panel.TranscriptText, Does.Contain("highlight_part(part_id=relief_valve)"));
        Assert.That(panel.TranscriptText, Does.Contain("There it is"));
    }

    [UnityTest]
    public IEnumerator StartProcedureTool_StartsTheRunnerAndShowsStepOne()
    {
        chat.Call("start_procedure", "{\"procedure_id\":\"relief_valve_replacement\"}").Say("Let's begin.");

        yield return Await(loop.AskAsync("Start the repair", speak: false));

        Assert.That(machine.Runner.State, Is.EqualTo(RunnerState.Running));
        Assert.That(panel.DetailText, Does.Contain("Step 1 of 7: Inspect the relief valve"));
    }

    [UnityTest]
    public IEnumerator ContextReachesTheModel_WithTelemetryAndManualSections()
    {
        chat.Say("The relief valve cartridge is stuck [fault.overpressure].");

        yield return Await(loop.AskAsync("Why is the pressure high?", speak: false));

        var context = chat.Requests.Single().Context;
        Assert.That(context, Does.Contain("pressure 6.8 bar (ALARM)"));
        Assert.That(context, Does.Contain("[fault.overpressure]"));
        Assert.That(chat.Requests.Single().SystemPrompt, Does.Contain("FM-200"));
    }

    [UnityTest]
    public IEnumerator LogNoteAndShowManual_UpdateThePanel()
    {
        chat.Call("log_note", "{\"text\":\"Cartridge corroded\"}").Call("show_manual", "{\"section_id\":\"safety.loto\"}").Say("Done.");

        yield return Await(loop.AskAsync("Note that and show lockout", speak: false));

        Assert.That(loop.Notes, Is.EqualTo(new[] { "Cartridge corroded" }));
        Assert.That(panel.DetailText, Does.Contain("[safety.loto] Lockout / tagout"));
    }

    private sealed class Scripted : IChatModel
    {
        private readonly Queue<ChatResponse> replies = new();
        private int id;
        public List<ChatRequest> Requests { get; } = new();
        public string Name => "scripted";

        public Scripted Call(string tool, string args)
        {
            replies.Enqueue(new ChatResponse("", new[] { new ToolCall($"t{++id}", tool, args) }, StopReason.ToolUse));
            return this;
        }

        public Scripted Say(string text)
        {
            replies.Enqueue(new ChatResponse(text, null, StopReason.EndTurn));
            return this;
        }

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(replies.Dequeue());
        }
    }
}
