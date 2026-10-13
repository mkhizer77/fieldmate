using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Fieldmate.Net
{
    /// <summary>
    /// Starts and stops the observer connection (design.md §5.7) with a Netcode for GameObjects manager it creates at
    /// runtime, so neither scene ships a NetworkManager. Unity Relay when the project is linked to Unity Gaming Services
    /// (join by a 6-character code, works across networks); otherwise a direct connection on the local network (join by
    /// "ip:port"). The trainee is the server; observers are clients.
    /// </summary>
    public sealed class ObserverNetwork : MonoBehaviour
    {
        public const ushort DefaultPort = 7788;
        public const int MaxObservers = 2;
        private const string RelayProtocol = "dtls";

        [SerializeField] private ushort port = DefaultPort;

        private NetworkManager manager;
        private UnityTransport transport;
        private NgoLink link;

        /// <summary>Null until started.</summary>
        public INetLink Link => link;

        public bool IsRunning => manager != null && manager.IsListening;
        public bool IsServer => manager != null && manager.IsServer;

        /// <summary>What an observer types to join: a Relay code, or "ip:port" on the local network.</summary>
        public string JoinCode { get; private set; } = string.Empty;

        /// <summary>Why Relay wasn't used, when it wasn't (shown next to the code).</summary>
        public string RelayNote { get; private set; } = string.Empty;

        public ushort Port
        {
            get => port;
            set => port = value;
        }

        /// <summary>The trainee's side. Returns the join code.</summary>
        public async Task<string> StartServerAsync(bool preferRelay, CancellationToken cancellationToken)
        {
            Shutdown();
            CreateManager();
            RelayNote = string.Empty;
            if (preferRelay)
            {
                try
                {
                    await SignInAsync();
                    var allocation = await RelayService.Instance.CreateAllocationAsync(MaxObservers);
                    var code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                    cancellationToken.ThrowIfCancellationRequested();
                    transport.SetRelayServerData(allocation.ToRelayServerData(RelayProtocol));
                    JoinCode = code;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    RelayNote = "Relay unavailable, local network only";
                    Debug.LogWarning($"[Observer] Relay unavailable ({e.GetType().Name}: {e.Message}); using the local network");
                }
            }

            if (string.IsNullOrEmpty(JoinCode))
            {
                transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
                JoinCode = $"{LocalAddress()}:{port}";
            }

            if (!manager.StartServer())
            {
                Shutdown();
                throw new InvalidOperationException("Couldn't start the observer server.");
            }

            link = new NgoLink(manager);
            Debug.Log($"[Observer] sharing, code {JoinCode}");
            return JoinCode;
        }

        /// <summary>The observer's side: a Relay code, or "ip:port" / "ip" on the local network.</summary>
        public async Task StartClientAsync(string code, CancellationToken cancellationToken)
        {
            Shutdown();
            CreateManager();
            code = (code ?? string.Empty).Trim();
            if (TryParseAddress(code, out var address, out var addressPort))
            {
                transport.SetConnectionData(address, addressPort);
            }
            else
            {
                await SignInAsync();
                var allocation = await RelayService.Instance.JoinAllocationAsync(code.ToUpperInvariant());
                cancellationToken.ThrowIfCancellationRequested();
                transport.SetRelayServerData(allocation.ToRelayServerData(RelayProtocol));
            }

            if (!manager.StartClient())
            {
                Shutdown();
                throw new InvalidOperationException("Couldn't start the observer client.");
            }

            link = new NgoLink(manager);
            JoinCode = code;
        }

        public void Shutdown()
        {
            link?.Dispose();
            link = null;
            JoinCode = string.Empty;
            if (manager != null)
            {
                var stopping = manager;
                manager = null;
                transport = null;
                if (stopping.IsListening)
                {
                    // Netcode finishes a shutdown on its next update, which is when it tells the other side; destroying
                    // the manager first would leave the observer waiting for a transport timeout.
                    stopping.Shutdown();
                    if (isActiveAndEnabled && !destroying)
                    {
                        StartCoroutine(DestroyWhenStopped(stopping));
                        return;
                    }
                }

                Destroy(stopping.gameObject);
            }
        }

        private bool destroying;

        private void OnDestroy()
        {
            destroying = true;
            Shutdown();
        }

        private static IEnumerator DestroyWhenStopped(NetworkManager stopping)
        {
            var until = Time.unscaledTime + 2f;
            while (stopping != null && (stopping.ShutdownInProgress || stopping.IsListening) && Time.unscaledTime < until)
            {
                yield return null;
            }

            if (stopping != null)
            {
                Destroy(stopping.gameObject);
            }
        }

        /// <summary>"192.168.1.20:7788", "192.168.1.20" (default port) → address and port; a Relay code → false.</summary>
        public static bool TryParseAddress(string code, out string address, out ushort addressPort)
        {
            address = null;
            addressPort = DefaultPort;
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var host = code.Trim();
            var colon = host.LastIndexOf(':');
            if (colon > 0)
            {
                if (!ushort.TryParse(host.Substring(colon + 1), out addressPort) || addressPort == 0)
                {
                    return false;
                }

                host = host.Substring(0, colon);
            }

            if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                addressPort = DefaultPort;
                return false;
            }

            address = ip.ToString();
            return true;
        }

        private void CreateManager()
        {
            // A root object: Netcode refuses a nested NetworkManager. Owned (and destroyed) by this component.
            var go = new GameObject("Observer Network Manager");
            transport = go.AddComponent<UnityTransport>();
            manager = go.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false, // the apps have different scenes; only named messages cross
                ConnectionApproval = false,
            };
        }

        private static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        // The address other devices on this network reach us at. Connecting a UDP socket sends nothing; it only picks
        // the outgoing interface.
        private static string LocalAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("10.255.255.255", 1);
                return ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
            }
            catch (SocketException)
            {
                return "127.0.0.1";
            }
        }
    }
}
