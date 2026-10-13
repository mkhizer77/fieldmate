using System.Collections;
using Fieldmate.Assistant;
using Fieldmate.Providers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Providers
{
    /// <summary>
    /// #101: a test run must never reach the live proxy (TTS credits, LLM cost). Locally the editor keys.json, on CI the
    /// public FieldmateProxy resource, used to wire every scene test that never called Configure to the real services.
    /// </summary>
    public class NoLiveProvidersTests
    {
        [Test]
        public void Load_DuringTests_GivesNoLiveProviders()
        {
            var providers = AssistantProviders.Load();

            Assert.That(providers.IsEnabled, Is.False, $"live providers from {providers.Source}");
            Assert.That(providers.DisabledReason, Does.Contain("tests"));
        }

        [UnityTest]
        public IEnumerator BenchScene_StartsWithTheAssistantOff_UntilATestConfiguresFakes()
        {
            yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
            yield return null;

            var loop = Object.FindAnyObjectByType<VoiceLoop>();
            Assert.That(loop.DisabledReason, Does.Contain("tests"));
        }
    }
}
