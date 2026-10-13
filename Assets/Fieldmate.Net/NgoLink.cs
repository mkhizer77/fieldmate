using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Fieldmate.Net;

/// <summary>
/// <see cref="INetLink"/> over a Netcode for GameObjects <see cref="NetworkManager"/>: one named message, reliable and
/// ordered (fragmented when large). No NetworkObjects or prefabs are involved, so trainee and observer apps share
/// nothing but this channel. Attach after the manager has started.
/// </summary>
public sealed class NgoLink : INetLink, IDisposable
{
    private readonly NetworkManager manager;
    private readonly List<ulong> peers = new();
    private byte[] inbound = new byte[1024];

    public NgoLink(NetworkManager manager)
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        manager.CustomMessagingManager.RegisterNamedMessageHandler(ObserverProtocol.Channel, OnMessage);
        manager.OnClientConnectedCallback += OnConnected;
        manager.OnClientDisconnectCallback += OnDisconnected;
        if (!manager.IsServer && manager.IsConnectedClient)
        {
            peers.Add(NetworkManager.ServerClientId);
        }
    }

    public IReadOnlyList<ulong> Peers => peers;

    public event Action<ulong> PeerConnected;
    public event Action<ulong> PeerDisconnected;
    public event Action<ulong, ArraySegment<byte>> Received;

    public void Dispose()
    {
        manager.OnClientConnectedCallback -= OnConnected;
        manager.OnClientDisconnectCallback -= OnDisconnected;
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(ObserverProtocol.Channel);
    }

    public void Send(ulong peer, ArraySegment<byte> data)
    {
        if (!manager.IsListening || !peers.Contains(peer))
        {
            return;
        }

        // Allocator.Temp is native scratch memory: no managed garbage per message.
        using var writer = new FastBufferWriter(data.Count + sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(data.Count);
        writer.WriteBytesSafe(data.Array, data.Count, data.Offset);
        manager.CustomMessagingManager.SendNamedMessage(ObserverProtocol.Channel, peer, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void OnMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        if (count < 0 || count > 64 * 1024)
        {
            return; // nothing in the protocol is this large
        }

        if (inbound.Length < count)
        {
            inbound = new byte[Math.Max(count, inbound.Length * 2)];
        }

        reader.ReadBytesSafe(ref inbound, count);
        Received?.Invoke(sender, new ArraySegment<byte>(inbound, 0, count));
    }

    private void OnConnected(ulong clientId)
    {
        // The server sees each observer; an observer sees only its own connection, which means "the server is there".
        var peer = manager.IsServer ? clientId : NetworkManager.ServerClientId;
        if (manager.IsServer && clientId == NetworkManager.ServerClientId)
        {
            return;
        }

        if (!peers.Contains(peer))
        {
            peers.Add(peer);
            PeerConnected?.Invoke(peer);
        }
    }

    private void OnDisconnected(ulong clientId)
    {
        var peer = manager.IsServer ? clientId : NetworkManager.ServerClientId;
        if (peers.Remove(peer))
        {
            PeerDisconnected?.Invoke(peer);
        }
    }
}
