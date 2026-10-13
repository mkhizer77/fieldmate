using Fieldmate.Assistant;
using Fieldmate.Providers;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.PlayMode;

/// <summary>
/// PlayMode tests load the bench scene for the machine, not for the guided setup (#71): it is off unless a test turns
/// it on (SetupFlowPlayModeTests), so placement behaves as before. No test reaches the live proxy (#101). Audio is
/// muted: batch runs otherwise click and play through the developer's speakers.
/// </summary>
[SetUpFixture]
public class PlayModeSetup
{
    [OneTimeSetUp]
    public void SetUp()
    {
        SetupFlow.AutoRun = false;
        AssistantProviders.LiveAllowed = false;
        AudioListener.volume = 0f; // muted, not paused: playback timing tests still need audio to advance
    }

    [OneTimeTearDown]
    public void Restore()
    {
        SetupFlow.AutoRun = true;
        AssistantProviders.LiveAllowed = true;
        AudioListener.volume = 1f;
    }
}

/// <summary>Guard: the PlayMode suite never makes a sound (Khizer heard clicks from headless runs, 2026-10-13).</summary>
public class SilentTestRunTests
{
    [Test]
    public void Audio_is_muted_while_tests_run()
    {
        Assert.That(AudioListener.volume, Is.Zero);
    }
}
