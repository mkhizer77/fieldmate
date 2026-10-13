using System;
using System.Linq;
using Fieldmate.AI;
using Fieldmate.Net;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Net;

/// <summary>#26: the remote observer protocol, the trainee-authoritative server and the observer's mirror.</summary>
public class ObserverTests
{
    private TelemetryModel telemetry;
    private ProcedureRunner runner;
    private LoopbackLink serverEnd;
    private LoopbackLink clientEnd;
    private ObserverServer server;
    private ObserverClient client;
    private double clock;

    [SetUp]
    public void SetUp()
    {
        telemetry = new TelemetryModel(FaultModel.CreateDefault());
        runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        (serverEnd, clientEnd) = LoopbackLink.Pair();
        server = new ObserverServer(serverEnd, telemetry, () => runner, "Pump skid PS-200");
        client = new ObserverClient(clientEnd) { Clock = () => clock };
    }

    private void Join()
    {
        clientEnd.Connect();
        serverEnd.Pump();
    }

    private void Tick(double t)
    {
        clock = t;
        server.Tick(t);
        serverEnd.Pump();
    }

    [Test]
    public void Codec_round_trips_every_field()
    {
        var w = new NetWriter(4);
        w.Byte(7);
        w.Bool(true);
        w.Short(-12);
        w.UInt(4_000_000_000);
        w.Float(-6.75f);
        w.String("Druckbegrenzungsventil ✓");
        var r = new NetReader(w.Segment);

        Assert.That(r.Byte(), Is.EqualTo(7));
        Assert.That(r.Bool(), Is.True);
        Assert.That(r.Short(), Is.EqualTo(-12));
        Assert.That(r.UInt(), Is.EqualTo(4_000_000_000));
        Assert.That(r.Float(), Is.EqualTo(-6.75f));
        Assert.That(r.String(), Is.EqualTo("Druckbegrenzungsventil ✓"));
        Assert.That(r.AtEnd, Is.True);
        Assert.Throws<FormatException>(() => r.Byte(), "reading past the end");
    }

    [Test]
    public void A_joining_observer_gets_the_hello_the_backlog_and_a_snapshot_at_once()
    {
        server.AddTranscript(new TranscriptEntry(TranscriptKind.User, "Why is the pressure high?"));
        server.AddTranscript(new TranscriptEntry(TranscriptKind.Assistant, "The relief valve is stuck."));

        Join();

        Assert.That(client.Hello.MachineName, Is.EqualTo("Pump skid PS-200"));
        Assert.That(client.Hello.Faults.Select(f => f.Id), Has.Member(FaultModel.Overpressure));
        Assert.That(client.Transcript.Select(l => l.Text), Is.EqualTo(new[] { "Why is the pressure high?", "The relief valve is stuck." }));
        Assert.That(client.HasSnapshot, Is.True);
        Assert.That(client.Snapshot.ProcedureTitle, Is.EqualTo(runner.Definition.Title));
        Assert.That(client.Snapshot.StepCount, Is.EqualTo(runner.Definition.Steps.Count));
    }

    [Test]
    public void Snapshots_stream_at_ten_hertz_only_while_someone_watches()
    {
        var sent = 0;
        clientEnd.Received += (_, _) => sent++;
        Tick(0.0);
        Assert.That(sent, Is.Zero, "nobody watching, nothing sent");

        Join();
        sent = 0;
        for (var t = 0.0; t < 1.0 - 1e-9; t += 1.0 / 72.0)
        {
            Tick(t);
        }

        Assert.That(sent, Is.InRange(9, 11), "one per 100 ms at 72 fps");
    }

    [Test]
    public void The_snapshot_mirrors_the_twin_and_the_procedure()
    {
        telemetry.Faults.Inject(FaultModel.Overpressure);
        telemetry.Settle();
        runner.Start(0);
        Join();
        Tick(1.0);

        var s = client.Snapshot;
        Assert.That(s.Telemetry[(int)TelemetryChannel.Pressure], Is.EqualTo(telemetry[TelemetryChannel.Pressure]));
        Assert.That(s.Status[(int)TelemetryChannel.Pressure], Is.EqualTo(ChannelStatus.Alarm));
        Assert.That(client.IsFaultActive(FaultModel.Overpressure), Is.True);
        Assert.That(client.IsFaultActive(FaultModel.Overheating), Is.False);
        Assert.That(s.Procedure, Is.EqualTo(RunnerState.Running));
        Assert.That(s.StepIndex, Is.Zero);
        Assert.That(s.StepTitle, Is.EqualTo(runner.CurrentStep.Title));
        Assert.That(s.Score, Is.EqualTo(-1));
        Assert.That(client.LastSnapshotTime, Is.EqualTo(1.0));
    }

    [Test]
    public void Transcript_lines_arrive_live_in_order_without_duplicates()
    {
        Join();
        server.AddTranscript(new TranscriptEntry(TranscriptKind.User, "a"));
        server.AddTranscript(new TranscriptEntry(TranscriptKind.Tool, "b"));
        serverEnd.Pump();

        clientEnd.Disconnect();
        server.AddTranscript(new TranscriptEntry(TranscriptKind.Assistant, "c"));
        Join(); // the backlog repeats a and b

        Assert.That(client.Transcript.Select(l => l.Text), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(client.Transcript[1].Kind, Is.EqualTo(TranscriptKind.Tool));
    }

    [Test]
    public void The_backlog_keeps_only_the_latest_lines()
    {
        for (var i = 0; i < ObserverProtocol.BacklogLines + 10; i++)
        {
            server.AddTranscript(new TranscriptEntry(TranscriptKind.Info, $"line {i}"));
        }

        Join();

        Assert.That(client.Transcript, Has.Count.EqualTo(ObserverProtocol.BacklogLines));
        Assert.That(client.Transcript[0].Text, Is.EqualTo("line 10"));
    }

    [Test]
    public void Fault_commands_change_the_twin_and_are_answered()
    {
        Join();
        var id = client.Send(ObserverCommand.InjectFault(FaultModel.Overheating));
        serverEnd.Pump();

        Assert.That(telemetry.Faults.IsActive(FaultModel.Overheating), Is.True);
        Assert.That(client.LastResult?.Id, Is.EqualTo(id));
        Assert.That(client.LastResult?.Accepted, Is.True);

        clock = 2;
        server.Tick(2);
        client.Send(ObserverCommand.ClearFault(FaultModel.Overheating));
        serverEnd.Pump();
        Assert.That(telemetry.Faults.IsActive(FaultModel.Overheating), Is.False);

        client.Send(ObserverCommand.InjectFault("meltdown"));
        serverEnd.Pump();
        Assert.That(client.LastResult?.Accepted, Is.False);
        StringAssert.Contains("Unknown fault", client.LastResult?.Message);
    }

    [Test]
    public void Hints_need_a_running_procedure_and_messages_need_text()
    {
        ObserverCommand? applied = null;
        server.CommandApplied += c => applied = c;
        Join();

        client.Send(ObserverCommand.HintStep());
        serverEnd.Pump();
        Assert.That(client.LastResult?.Accepted, Is.False, "nothing running");
        Assert.That(applied, Is.Null);

        runner.Start(0);
        client.Send(ObserverCommand.HintStep("Look at the gauge first."));
        serverEnd.Pump();
        Assert.That(applied?.Kind, Is.EqualTo(ObserverCommandKind.HintStep));
        Assert.That(applied?.Argument, Is.EqualTo("Look at the gauge first."));

        clock = 5;
        server.Tick(5);
        client.Send(ObserverCommand.SendMessage("   "));
        serverEnd.Pump();
        Assert.That(client.LastResult?.Accepted, Is.False);

        client.Send(ObserverCommand.SendMessage("Good job,\n<size=500>keep going</size>"));
        serverEnd.Pump();
        Assert.That(applied?.Kind, Is.EqualTo(ObserverCommandKind.SendMessage));
        Assert.That(applied?.Argument, Is.EqualTo("Good job, size=500keep going/size"), "no rich text reaches the headset");
    }

    [Test]
    public void An_observer_flooding_commands_is_throttled()
    {
        Join();
        var accepted = 0;
        for (var i = 0; i < 10; i++)
        {
            client.Send(ObserverCommand.SendMessage($"hi {i}"));
            serverEnd.Pump();
            accepted += client.LastResult?.Accepted == true ? 1 : 0;
        }

        Assert.That(accepted, Is.EqualTo(ObserverServer.CommandsPerSecond));
        StringAssert.Contains("Too many", client.LastResult?.Message);

        Tick(1.5);
        client.Send(ObserverCommand.SendMessage("later"));
        serverEnd.Pump();
        Assert.That(client.LastResult?.Accepted, Is.True, "a new second, a new allowance");
    }

    [Test]
    public void Garbage_and_trainee_only_messages_from_an_observer_are_dropped()
    {
        var warnings = 0;
        server.Warning += _ => warnings++;
        Join();

        clientEnd.Send(0, new ArraySegment<byte>(new byte[] { (byte)MessageType.Command, 1 }));
        var fake = new NetWriter();
        new ObserverSnapshot().Write(fake);
        clientEnd.Send(0, fake.Segment);
        Assert.DoesNotThrow(() => serverEnd.Pump());

        Assert.That(warnings, Is.EqualTo(2));
    }

    [Test]
    public void A_trainee_on_another_protocol_version_is_shown_but_not_applied()
    {
        var hello = new NetWriter();
        new ObserverHello { Version = ObserverProtocol.Version + 1 }.Write(hello);
        var (otherServer, otherClient) = LoopbackLink.Pair();
        var observer = new ObserverClient(otherClient);
        otherClient.Connect();
        otherServer.Send(1, hello.Segment);
        var snapshot = new NetWriter();
        new ObserverSnapshot { StepTitle = "x" }.Write(snapshot);
        otherServer.Send(1, snapshot.Segment);
        otherServer.Pump();

        Assert.That(observer.Incompatible, Is.True);
        Assert.That(observer.HasSnapshot, Is.False);
        Assert.That(observer.Send(ObserverCommand.HintStep()), Is.Zero, "no commands to an incompatible trainee");
    }

    [Test]
    public void Building_and_writing_a_snapshot_allocates_nothing()
    {
        runner.Start(0);
        var snapshot = new ObserverSnapshot();
        var writer = new NetWriter(1024);
        snapshot.Capture(telemetry, runner, 0f);
        snapshot.Write(writer.Reset()); // warm up

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            snapshot.Capture(telemetry, runner, i * 0.1f);
            snapshot.Write(writer.Reset());
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        Assert.That(writer.Length, Is.LessThan(300), "a snapshot is a few hundred bytes");
    }

    [TestCase("hello", 200, "hello")]
    [TestCase("  a \t\n b  ", 200, "a b")]
    [TestCase("<b>bold</b>", 200, "bbold/b")]
    [TestCase("abcdef", 3, "abc")]
    [TestCase("ab cd", 3, "ab")]
    [TestCase(null, 10, "")]
    public void Observer_text_is_cleaned_before_it_reaches_the_headset(string input, int max, string expected) =>
        Assert.That(ObserverServer.Clean(input, max), Is.EqualTo(expected));
}
