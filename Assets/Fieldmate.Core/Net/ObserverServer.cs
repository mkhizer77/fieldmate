using System;
using System.Collections.Generic;
using System.Text;
using Fieldmate.AI;
using Fieldmate.Procedures;
using Fieldmate.Twin;

namespace Fieldmate.Net;

/// <summary>The trainee's answer to a command, matched by id.</summary>
public readonly struct CommandResult
{
    public CommandResult(uint id, bool accepted, string message)
    {
        Id = id;
        Accepted = accepted;
        Message = message ?? string.Empty;
    }

    public uint Id { get; }
    public bool Accepted { get; }
    public string Message { get; }

    public void Write(NetWriter w)
    {
        w.Byte((byte)MessageType.CommandResult);
        w.UInt(Id);
        w.Bool(Accepted);
        w.String(Message, ObserverProtocol.MaxTextChars);
    }

    public static CommandResult Read(NetReader r) => new(r.UInt(), r.Bool(), r.String());
}

/// <summary>
/// The trainee's side of the remote observer (design.md §5.7). Authoritative: it streams the twin and procedure to every
/// observer at 10 Hz and every transcript line as it happens, and treats observer commands as requests, validated,
/// rate-limited and answered. Nothing an observer sends can block or crash the headset: malformed or unexpected
/// messages are dropped. Sending reuses one buffer; no allocations per snapshot.
/// </summary>
public sealed class ObserverServer : IDisposable
{
    /// <summary>Commands per observer per second; more are refused.</summary>
    public const int CommandsPerSecond = 4;

    private readonly INetLink link;
    private readonly TelemetryModel telemetry;
    private readonly Func<ProcedureRunner> runner;
    private readonly ObserverHello hello;
    private readonly ObserverSnapshot snapshot = new();
    private readonly NetWriter writer = new(1024);
    private readonly Queue<TranscriptLine> backlog = new();
    private readonly Dictionary<ulong, (double windowStart, int count)> rate = new();
    private uint transcriptSequence;
    private double nextSnapshot;
    private double now;

    public ObserverServer(INetLink link, TelemetryModel telemetry, Func<ProcedureRunner> runner, string machineName)
    {
        this.link = link ?? throw new ArgumentNullException(nameof(link));
        this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        this.runner = runner ?? (() => null);
        hello = new ObserverHello { MachineName = machineName ?? string.Empty };
        foreach (var fault in telemetry.Faults.Definitions)
        {
            hello.Faults.Add((fault.Id, fault.Name));
        }

        link.PeerConnected += OnPeerConnected;
        link.Received += OnReceived;
    }

    /// <summary>A hint or message the scene should present (faults are applied here, to the twin).</summary>
    public event Action<ObserverCommand> CommandApplied;

    /// <summary>Something an observer sent was dropped (for the log).</summary>
    public event Action<string> Warning;

    public int ObserverCount => link.Peers.Count;
    public ObserverSnapshot LastSnapshot => snapshot;

    public void Dispose()
    {
        link.PeerConnected -= OnPeerConnected;
        link.Received -= OnReceived;
    }

    /// <summary>Call every frame with the session clock; sends a snapshot every 100 ms while anyone watches.</summary>
    public void Tick(double time)
    {
        now = time;
        if (link.Peers.Count == 0 || time < nextSnapshot)
        {
            return;
        }

        nextSnapshot = time + 1.0 / ObserverProtocol.SnapshotHz;
        snapshot.Capture(telemetry, runner(), (float)time);
        snapshot.Write(writer.Reset());
        Broadcast();
    }

    /// <summary>Records a transcript line and sends it to everyone watching.</summary>
    public void AddTranscript(TranscriptEntry entry)
    {
        var line = new TranscriptLine(++transcriptSequence, entry.Kind, entry.Text);
        backlog.Enqueue(line);
        while (backlog.Count > ObserverProtocol.BacklogLines)
        {
            backlog.Dequeue();
        }

        if (link.Peers.Count > 0)
        {
            line.Write(writer.Reset());
            Broadcast();
        }
    }

    private void OnPeerConnected(ulong peer)
    {
        hello.Write(writer.Reset());
        link.Send(peer, writer.Segment);
        foreach (var line in backlog)
        {
            line.Write(writer.Reset());
            link.Send(peer, writer.Segment);
        }

        snapshot.Capture(telemetry, runner(), (float)now);
        snapshot.Write(writer.Reset());
        link.Send(peer, writer.Segment);
    }

    private void OnReceived(ulong peer, ArraySegment<byte> data)
    {
        ObserverCommand command;
        try
        {
            var reader = new NetReader(data);
            if ((MessageType)reader.Byte() != MessageType.Command)
            {
                Warning?.Invoke($"observer {peer} sent a message that only the trainee may send");
                return;
            }

            command = ObserverCommand.Read(reader);
        }
        catch (FormatException)
        {
            Warning?.Invoke($"observer {peer} sent a malformed message");
            return;
        }

        var result = Apply(peer, command);
        result.Write(writer.Reset());
        link.Send(peer, writer.Segment);
    }

    private CommandResult Apply(ulong peer, ObserverCommand command)
    {
        if (!TakeToken(peer))
        {
            return new CommandResult(command.Id, false, "Too many commands; wait a moment.");
        }

        var faults = telemetry.Faults;
        switch (command.Kind)
        {
            case ObserverCommandKind.InjectFault:
            case ObserverCommandKind.ClearFault:
                if (!faults.TryGetDefinition(command.Argument, out var fault))
                {
                    return new CommandResult(command.Id, false, $"Unknown fault '{Clean(command.Argument, 40)}'.");
                }

                var inject = command.Kind == ObserverCommandKind.InjectFault;
                if (faults.IsActive(fault.Id) == inject)
                {
                    return new CommandResult(command.Id, true, $"{fault.Name} is already {(inject ? "active" : "cleared")}.");
                }

                if (inject)
                {
                    faults.Inject(fault.Id);
                }
                else
                {
                    faults.Clear(fault.Id);
                }

                CommandApplied?.Invoke(command);
                return new CommandResult(command.Id, true, $"{fault.Name} {(inject ? "injected" : "cleared")}.");

            case ObserverCommandKind.HintStep:
                if (runner() is not { State: RunnerState.Running })
                {
                    return new CommandResult(command.Id, false, "No procedure is running.");
                }

                CommandApplied?.Invoke(new ObserverCommand(command.Kind, Clean(command.Argument, ObserverProtocol.MaxTextChars), command.Id));
                return new CommandResult(command.Id, true, "Hint shown.");

            case ObserverCommandKind.SendMessage:
                var text = Clean(command.Argument, ObserverProtocol.MaxTextChars);
                if (text.Length == 0)
                {
                    return new CommandResult(command.Id, false, "The message is empty.");
                }

                CommandApplied?.Invoke(new ObserverCommand(command.Kind, text, command.Id));
                return new CommandResult(command.Id, true, "Message delivered.");

            default:
                return new CommandResult(command.Id, false, "Unknown command.");
        }
    }

    private bool TakeToken(ulong peer)
    {
        rate.TryGetValue(peer, out var window);
        if (now - window.windowStart >= 1.0)
        {
            window = (now, 0);
        }

        if (window.count >= CommandsPerSecond)
        {
            rate[peer] = window;
            return false;
        }

        rate[peer] = (window.windowStart, window.count + 1);
        return true;
    }

    /// <summary>
    /// Text from an observer, safe to show in the headset: control characters and rich-text brackets removed (a "&lt;size&gt;"
    /// tag must not take over the panel), whitespace collapsed, length capped.
    /// </summary>
    public static string Clean(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(text.Length, maxChars));
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (c is '<' or '>')
            {
                continue;
            }

            if (sb.Length + (space ? 2 : 1) > maxChars)
            {
                break;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private void Broadcast()
    {
        var peers = link.Peers;
        for (var i = 0; i < peers.Count; i++)
        {
            link.Send(peers[i], writer.Segment);
        }
    }
}
