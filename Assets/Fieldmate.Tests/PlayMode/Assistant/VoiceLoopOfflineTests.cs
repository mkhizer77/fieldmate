using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Assistant;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#16: in the scene, a model that keeps failing turns the panel's banner on and answers from the script.</summary>
public class VoiceLoopOfflineTests
{
    private sealed class Failing : IChatModel
    {
        public int Calls;
        public string Name => "failing";
        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new ProviderException(ProviderException.Network, "airplane mode");
        }
    }

    private static IEnumerator Await(Task task)
    {
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted) throw task.Exception!.GetBaseException();
    }

    [UnityTest]
    public IEnumerator Failing_model_shows_the_offline_banner_and_scripted_answers()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        var loop = Object.FindAnyObjectByType<VoiceLoop>();
        var panel = Object.FindAnyObjectByType<AssistantPanel>();
        var chat = new Failing();
        loop.Configure(chat, null, null);

        yield return Await(loop.AskAsync("hello", speak: false));
        Assert.That(panel.BannerText, Is.Empty, "one failure is an error line, not offline");
        yield return Await(loop.AskAsync("where is the pressure gauge?", speak: false));
        Assert.That(panel.BannerText, Does.Contain("Offline"));
        Assert.That(panel.TranscriptText, Does.Contain("highlighted"));
        Assert.That(Object.FindAnyObjectByType<PartHighlighter>().ActivePartId, Is.EqualTo("pressure_gauge"));
        var calls = chat.Calls;
        yield return Await(loop.AskAsync("what now?", speak: false));
        Assert.That(chat.Calls, Is.EqualTo(calls), "no model call while offline");
        Assert.That(panel.TranscriptText, Does.Contain("Press Start"));
    }
}
