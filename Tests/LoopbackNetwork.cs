using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FDG.Network;
using FDG.Network.Connection;

namespace FDG.Tests
{
    /// <summary>
    /// Shared in-process, synchronous transport double for the lobby/message-bus tests — the
    /// multi-client replacement for the single-client <c>LoopbackHost</c>/<c>LoopbackClient</c> pair that
    /// was copy-pasted into six test files (#188, and the fixture half of #065).
    ///
    /// <para>The copies were structurally incapable of catching the bug class #188 exists for: each held
    /// ONE client field and <c>SendCommandToSingleClientAsync</c> ignored its <see cref="ConnectionID"/>
    /// argument entirely, delivering to that one client. Targeted sends — the QF5 PlayerID assignment,
    /// #088 per-player request routing, #105 log de-dup — therefore passed no matter where they were
    /// addressed. Here every client has its own ConnectionID and a targeted send reaches exactly one.</para>
    ///
    /// <para>Deliberately NOT modelled: the host noticing a client's socket close. The real host learns
    /// that from its read loop; this double has none, so <see cref="LoopbackNetworkClient.Disconnect"/>
    /// raises only the client-side event (as the original doubles did — the teardown tests rely on the
    /// host still broadcasting to a disposed client VM to prove the VM itself ignores it). Use
    /// <see cref="LoopbackNetworkHost.DropClient"/> to simulate the host-side notice explicitly.</para>
    /// </summary>
    internal sealed class LoopbackNetworkHost : INetworkHost
    {
        // Insertion-ordered so roster-order assertions are deterministic; broadcasts iterate a snapshot
        // because a handler can connect or drop a client while one is in flight.
        private readonly List<LoopbackNetworkClient> _clients = new List<LoopbackNetworkClient>();

        public event Action<ConnectionID>? OnNewClientConnected;
        public event Action<ConnectionID>? OnClientDisconnected;
        public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived;

        /// <summary>Every client still attached, in connection order.</summary>
        public IReadOnlyList<LoopbackNetworkClient> Clients => _clients;

        /// <summary>Connections passed to <see cref="MarkClientAuthenticated"/> (#266), in call order.</summary>
        public List<ConnectionID> AuthenticatedConnections { get; } = new List<ConnectionID>();

        /// <summary>Connections passed to <see cref="DisconnectClient"/> (QF2 eviction), in call order.</summary>
        public List<ConnectionID> EvictedConnections { get; } = new List<ConnectionID>();

        /// <summary>Times <see cref="Stop"/> was called — the #279 teardown pin.</summary>
        public int StopCount { get; private set; }

        /// <summary>Targeted sends addressed to a connection this host doesn't have. Always expect 0:
        /// a nonzero count means routing computed a stale or bogus ConnectionID and the message
        /// silently went nowhere.</summary>
        public int UnroutableSendCount { get; private set; }

        /// <summary>
        /// Attaches a new client and raises <see cref="OnNewClientConnected"/>. Connect AFTER constructing
        /// the host view model if you want it to observe the event (the greeting-timeout path, QF2);
        /// connecting first mirrors the old doubles, where nothing was subscribed yet.
        /// </summary>
        public LoopbackNetworkClient Connect(string label = "client")
        {
            var client = new LoopbackNetworkClient(this, label);
            _clients.Add(client);
            OnNewClientConnected?.Invoke(client.ConnectionID);
            return client;
        }

        /// <summary>
        /// Simulates the host's read loop noticing a client is gone: detaches it and raises
        /// <see cref="OnClientDisconnected"/>. Separate from the client calling
        /// <see cref="LoopbackNetworkClient.Disconnect"/>, which this double cannot observe.
        /// </summary>
        public void DropClient(LoopbackNetworkClient client)
        {
            if (_clients.Remove(client) == false) return;
            OnClientDisconnected?.Invoke(client.ConnectionID);
        }

        public Task StartAsync() => Task.CompletedTask;

        public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled)
        {
            foreach (LoopbackNetworkClient client in _clients.ToArray())
                client.Deliver(Copy(data));
            return Task.CompletedTask;
        }

        public Task SendCommandToSingleClientAsync(ConnectionID client, ArraySegment<byte> data, bool isPooled)
        {
            LoopbackNetworkClient? target = _clients.FirstOrDefault(c => c.ConnectionID == client);
            if (target == null)
            {
                // A real host drops a send to a connection it no longer holds; counted so a test can
                // assert routing addressed someone real rather than quietly hitting nobody.
                UnroutableSendCount++;
                return Task.CompletedTask;
            }

            target.Deliver(Copy(data));
            return Task.CompletedTask;
        }

        public void DisconnectClient(ConnectionID client)
        {
            EvictedConnections.Add(client);
            LoopbackNetworkClient? target = _clients.FirstOrDefault(c => c.ConnectionID == client);
            if (target != null) DropClient(target);
        }

        public void MarkClientAuthenticated(ConnectionID client) => AuthenticatedConnections.Add(client);

        public void Stop() => StopCount++;

        // Raised by a client sending to the host, tagged with that client's own ConnectionID — the part
        // the single-client doubles got wrong, since they had only one ConnectionID to report.
        internal void ReceiveFromClient(ArraySegment<byte> data, ConnectionID from) =>
            OnMessageReceived?.Invoke(data, from);

        // The real transport hands the receiver its own buffer; copying keeps a pooled/reused segment
        // from mutating under a handler.
        internal static ArraySegment<byte> Copy(ArraySegment<byte> data) =>
            new ArraySegment<byte>(data.ToArray());
    }

    internal sealed class LoopbackNetworkClient : INetworkClient
    {
        private readonly LoopbackNetworkHost _host;

        public event Action<ArraySegment<byte>>? OnMessageReceived;
        public event Action? OnDisconnected;

        /// <summary>Identifies this client to the host. Unique per client — the whole point of the fixture.</summary>
        public ConnectionID ConnectionID { get; } = new ConnectionID(Guid.NewGuid());

        /// <summary>Test-facing name, for assertion messages.</summary>
        public string Label { get; }

        /// <summary>Frames this client received, broadcast or targeted. Lets a test assert that a
        /// targeted send reached exactly one client without decoding the wire bytes.</summary>
        public int ReceivedFrameCount { get; private set; }

        /// <summary>Times <see cref="Disconnect"/> was called — the client half of the #279 teardown pin.</summary>
        public int DisconnectCount { get; private set; }

        internal LoopbackNetworkClient(LoopbackNetworkHost host, string label)
        {
            _host = host;
            Label = label;
        }

        public Task<bool> ConnectAsync(IPAddress serverIP, int port = NetworkProtocol.DefaultPort) =>
            Task.FromResult(true);

        public Task SendCommandToHost(ArraySegment<byte> command, bool isPooled)
        {
            _host.ReceiveFromClient(LoopbackNetworkHost.Copy(command), ConnectionID);
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
            DisconnectCount++;
            OnDisconnected?.Invoke();
        }

        internal void Deliver(ArraySegment<byte> data)
        {
            ReceivedFrameCount++;
            OnMessageReceived?.Invoke(data);
        }
    }
}
