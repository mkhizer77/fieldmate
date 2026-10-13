using System;
using System.IO;
using Fieldmate.AI;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using UnityEngine;

namespace Fieldmate.Net
{
    /// <summary>
    /// Runs the observer against a simulated trainee when started with <c>-preview</c>, so the dashboard can be tried
    /// (and checked) without a headset: the real <see cref="ObserverServer"/> over an in-memory link, a twin with the demo
    /// fault, a procedure a few steps in and a short conversation. Commands work against the simulated twin.
    /// <c>-screenshot &lt;path&gt;</c> saves a PNG after a few seconds and quits.
    /// </summary>
    public sealed class ObserverPreview : MonoBehaviour
    {
        [SerializeField] private ObserverDashboard dashboard;

        private LoopbackLink serverEnd;
        private ObserverServer server;
        private TelemetryModel telemetry;
        private ProcedureRunner runner;
        private string screenshotPath;
        private float screenshotAt = float.PositiveInfinity;
        private bool quitAfterShot;

        public bool IsRunning => server != null;

        public void Configure(ObserverDashboard observerDashboard) => dashboard = observerDashboard;

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            var preview = false;
            for (var i = 0; i < args.Length; i++)
            {
                preview |= args[i] == "-preview";
                if (args[i] == "-screenshot" && i + 1 < args.Length)
                {
                    screenshotPath = Path.GetFullPath(args[i + 1]);
                    screenshotAt = Time.unscaledTime + 3f;
                    quitAfterShot = true;
                }
            }

            if (preview)
            {
                Run();
            }
        }

        /// <summary>Starts the simulated trainee and points the dashboard at it.</summary>
        public void Run()
        {
            telemetry = new TelemetryModel(FaultModel.CreateDefault());
            telemetry.Faults.Inject(FaultModel.Overpressure);
            telemetry.Settle();
            runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
            DemoProcedures.ApplyInitialStates(runner);
            runner.Start(0);
            runner.Handle(InteractionEvent.Gaze(2, "relief_valve", 3f));
            runner.Handle(InteractionEvent.State(9, "main_breaker", "locked"));

            var (serverLink, clientLink) = LoopbackLink.Pair();
            serverEnd = serverLink;
            server = new ObserverServer(serverLink, telemetry, () => runner, "Pump skid (preview)");
            server.AddTranscript(new TranscriptEntry(TranscriptKind.User, "Why is the pressure so high?"));
            server.AddTranscript(new TranscriptEntry(TranscriptKind.Tool, "read_telemetry()"));
            server.AddTranscript(new TranscriptEntry(TranscriptKind.Assistant, "Pressure is 6.8 bar, in alarm: the relief valve is stuck [fault.overpressure]. Let's lock out the breaker first."));
            server.AddTranscript(new TranscriptEntry(TranscriptKind.User, "Done, what next?"));
            server.AddTranscript(new TranscriptEntry(TranscriptKind.Assistant, "Good. Now close the inlet valve so the line can bleed down."));
            server.CommandApplied += c => server.AddTranscript(new TranscriptEntry(TranscriptKind.Info, $"(preview) {c.Kind}: {c.Argument}"));
            dashboard.Attach(clientLink);
            clientLink.Connect();
            serverEnd.Pump();
        }

        private void Update()
        {
            if (server != null)
            {
                telemetry.Step(Time.deltaTime);
                server.Tick(Time.unscaledTimeAsDouble);
                serverEnd.Pump();
            }

            if (Time.unscaledTime >= screenshotAt)
            {
                screenshotAt = float.PositiveInfinity;
                ScreenCapture.CaptureScreenshot(screenshotPath);
                Debug.Log($"[Observer] screenshot {screenshotPath}");
                if (quitAfterShot)
                {
                    Invoke(nameof(Quit), 1f);
                }
            }
        }

        private void Quit() => Application.Quit();

        private void OnDestroy() => server?.Dispose();
    }
}
