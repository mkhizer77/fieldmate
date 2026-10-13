using Fieldmate.Assistant;
using Fieldmate.Providers;
using NUnit.Framework;

namespace Fieldmate.Tests.PlayMode;

/// <summary>
/// PlayMode tests load the bench scene for the machine, not for the guided setup (#71): it is off unless a test turns
/// it on (SetupFlowPlayModeTests), so placement behaves as before. No test reaches the live proxy (#101).
/// </summary>
[SetUpFixture]
public class PlayModeSetup
{
    [OneTimeSetUp]
    public void SetUp()
    {
        SetupFlow.AutoRun = false;
        AssistantProviders.LiveAllowed = false;
    }

    [OneTimeTearDown]
    public void Restore()
    {
        SetupFlow.AutoRun = true;
        AssistantProviders.LiveAllowed = true;
    }
}
