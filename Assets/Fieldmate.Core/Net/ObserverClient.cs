using System;
using System.Collections.Generic;

namespace Fieldmate.Net;

/// <summary>
/// The observer's side (design.md §5.7): mirrors what the trainee streams (hello, snapshots, transcript) and sends
/// commands. Read-only towards the twin; everything it shows comes from the trainee.
/// </summary>
public sealed class ObserverClient : IDisposable
{
    public const int MaxTranscript = 200;

    private readonly INetLink link;
    private readonly NetWriter writer = new(256);
    private readonly List<TranscriptLine> transcript = new();
    private readonly Dictionary<uint, ObserverCommand> pending = new();
    private uint lastSequence;
    private uint nextCommandId;

    public ObserverClient(INetLink link)
    {
        this.link = link ?? throw new ArgumentNullException(nameof(link));
        link.Received += OnReceived;
        link.PeerDisconnected += OnDisconnected;
    }

    /// <summary>Anything changed: snapshot, transcript, hello, a command result or the connection.</summary>
    public event Action Changed;

    public ObserverHello Hello { get; private set; }
    public ObserverSnapshot Snapshot { get; } = new();
    public bool HasSnapshot { get; private set; }
    public IReadOnlyList<TranscriptLine> Transcript => transcript;
    public CommandResult? LastResult { get; private set; }

    /// <summary>The trainee runs another protocol version; nothing it sends is applied.</summary>
    public bool Incompatible { get; private set; }

    public bool Connected => link.Peers.Count > 0;

    /// <summary><see cref="Clock"/> at the last snapshot, to show a stale connection.</summary>
    public double LastSnapshotTime { get; private set; } = double.NegativeInfinity;

    /// <summary>Local clock for <see cref="LastSnapshotTime"/>; set by the app.</summary>
    public Func<double> Clock { get; set; } = () => 0d;

    public void Dispose()
    {
        link.Received -= OnReceived;
        link.PeerDisconnected -= OnDisconnected;
    }

    /// <summary>Sends a command to the trainee; returns its id (0 when not connected).</summary>
    public uint Send(ObserverCommand command)
    {
        if (!Connected || Incompatible)
        {
            return 0;
        }

        var withId = command.WithId(++nextCommandId);
        pending[withId.Id] = withId;
        withId.Write(writer.Reset());
        link.Send(link.Peers[0], writer.Segment);
        return withId.Id;
    }

    /// <summary>True while the fault (by id from the hello) is active in the last snapshot.</summary>
    public bool IsFaultActive(string faultId)
    {
        if (Hello == null)
        {
            return false;
        }

        for (var i = 0; i < Hello.Faults.Count && i < 32; i++)
        {
            if (Hello.Faults[i].Id == faultId)
            {
                return (Snapshot.ActiveFaults & (1u << i)) != 0;
            }
        }

        return false;
    }

    private void OnDisconnected(ulong peer) => Changed?.Invoke();

    private void OnReceived(ulong peer, ArraySegment<byte> data)
    {
        try
        {
            var reader = new NetReader(data);
            var type = (MessageType)reader.Byte();
            if (type == MessageType.Hello)
            {
                Hello = ObserverHello.Read(reader);
                Incompatible = Hello.Version != ObserverProtocol.Version;
                transcript.Clear(); // the backlog that follows is the whole story so far
                lastSequence = 0;
            }
            else if (Incompatible || Hello == null)
            {
                return;
            }
            else
            {
                switch (type)
                {
                    case MessageType.Snapshot:
                        Snapshot.Read(reader);
                        HasSnapshot = true;
                        LastSnapshotTime = Clock();
                        break;
                    case MessageType.Transcript:
                        AddLine(TranscriptLine.Read(reader));
                        break;
                    case MessageType.CommandResult:
                        var result = CommandResult.Read(reader);
                        pending.Remove(result.Id);
                        LastResult = result;
                        break;
                    default:
                        return;
                }
            }
        }
        catch (FormatException)
        {
            return;
        }

        Changed?.Invoke();
    }

    private void AddLine(TranscriptLine line)
    {
        if (line.Sequence <= lastSequence)
        {
            return; // already have it
        }

        lastSequence = line.Sequence;
        transcript.Add(line);
        if (transcript.Count > MaxTranscript)
        {
            transcript.RemoveAt(0);
        }
    }
}
