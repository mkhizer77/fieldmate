using Fieldmate.Assistant;
using NUnit.Framework;

namespace Fieldmate.Tests.PlayMode;

/// <summary>
/// PlayMode tests load the bench scene for the machine, not for the guided setup (#71): it is off unless a test turns
/// it on (SetupFlowPlayModeTests), so placement behaves as before.
/// </summary>
[SetUpFixture]
public class PlayModeSetup
{
    [OneTimeSetUp]
    public void SkipGuidedSetup() => SetupFlow.AutoRun = false;

    [OneTimeTearDown]
    public void Restore() => SetupFlow.AutoRun = true;
}
