using System;
using System.Collections.Generic;

namespace Fieldmate.Net;

/// <summary>
/// A reliable, ordered message pipe between the trainee (server) and observers (clients). Netcode for GameObjects
/// implements it in Fieldmate.Net (Unity); <see cref="LoopbackLink"/> implements it in memory for tests.
/// Peer 0 is the server.
/// </summary>
public interface INetLink
{
    /// <summary>Connected peers other than this end (the server sees observers; an observer sees the server).</summary>
    IReadOnlyList<ulong> Peers { get; }

    /// <summary>Queues a message; never blocks. The data is copied before this returns.</summary>
    void Send(ulong peer, ArraySegment<byte> data);

    event Action<ulong> PeerConnected;
    event Action<ulong> PeerDisconnected;
    event Action<ulong, ArraySegment<byte>> Received;
}

/// <summary>Two in-memory ends of an <see cref="INetLink"/>; messages are delivered on <see cref="Pump"/>.</summary>
public sealed class LoopbackLink : INetLink
{
    private readonly List<ulong> peers = new();
    private readonly Queue<(ulong from, byte[] data)> inbox = new();
    private LoopbackLink other;

    private LoopbackLink(ulong id) => Id = id;

    public ulong Id { get; }
    public IReadOnlyList<ulong> Peers => peers;

    public event Action<ulong> PeerConnected;
    public event Action<ulong> PeerDisconnected;
    public event Action<ulong, ArraySegment<byte>> Received;

    /// <summary>A server end (peer 0) and an unconnected client end with the given id.</summary>
    public static (LoopbackLink server, LoopbackLink client) Pair(ulong clientId = 1)
    {
        var server = new LoopbackLink(0);
        var client = new LoopbackLink(clientId);
        server.other = client;
        client.other = server;
        return (server, client);
    }

    public void Connect()
    {
        if (peers.Contains(other.Id))
        {
            return;
        }

        peers.Add(other.Id);
        other.peers.Add(Id);
        other.PeerConnected?.Invoke(Id);
        PeerConnected?.Invoke(other.Id);
    }

    public void Disconnect()
    {
        if (!peers.Remove(other.Id))
        {
            return;
        }

        other.peers.Remove(Id);
        other.PeerDisconnected?.Invoke(Id);
        PeerDisconnected?.Invoke(other.Id);
    }

    public void Send(ulong peer, ArraySegment<byte> data)
    {
        if (peer != other.Id || !peers.Contains(peer))
        {
            return;
        }

        var copy = new byte[data.Count];
        Array.Copy(data.Array!, data.Offset, copy, 0, data.Count);
        other.inbox.Enqueue((Id, copy));
    }

    /// <summary>Delivers everything queued on both ends.</summary>
    public void Pump()
    {
        while (inbox.Count > 0 || other.inbox.Count > 0)
        {
            Deliver(this);
            Deliver(other);
        }
    }

    private static void Deliver(LoopbackLink end)
    {
        while (end.inbox.Count > 0)
        {
            var (from, data) = end.inbox.Dequeue();
            end.Received?.Invoke(from, new ArraySegment<byte>(data));
        }
    }
}
