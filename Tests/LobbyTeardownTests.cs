using System;
using System.Net;
using System.Threading.Tasks;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Pins the lobby view models' full network teardown (#279) over the same in-process loopback
    /// doubles as <see cref="LobbyJoinGateTests"/>. The original bug: a replaced host lobby kept its
    /// greeting handler and listener alive, so it accepted joins as a half-alive zombie (roster updates
    /// went to a lobby no UI was bound to, client chat was dropped, and disconnected clients were never
    /// removed), while a client that backed out never closed its connection and haunted the host's
    /// roster as a ghost slot.
    /// </summary>
    [TestFixture]
    public class LobbyTeardownTests
    {
        [Test]
        public void DisposedHostLobby_IgnoresLateGreeting()
        {
            (LoopbackHost loopbackHost, LoopbackClient loopbackClient) = WireLoopback();
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);

            hostVm.Dispose();

            // The client's constructor sends its greeting synchronously over the loopback; a disposed
            // host lobby must no longer be listening for it.
            var clientVm = new LobbyViewModel_Client("Latecomer", loopbackClient, "");

            Assert.That(hostVm.PlayerInfos.Count, Is.EqualTo(1),
                "A disposed host lobby must not add joiners to its roster.");
            Assert.That(clientVm.JoinResultTask.IsCompleted, Is.False,
                "A disposed host lobby must not answer a join handshake.");
        }

        [Test]
        public void HostDispose_StopsTheNetworkHost_Once()
        {
            (LoopbackHost loopbackHost, LoopbackClient _) = WireLoopback();
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);

            hostVm.Dispose();
            hostVm.Dispose(); //Reached from several app paths, so it must be idempotent.

            Assert.That(loopbackHost.StopCount, Is.EqualTo(1),
                "Disposing the host lobby must stop the listener (releasing the port), exactly once.");
        }

        [Test]
        public async Task ClientDispose_ClosesTheConnection_WithoutRaisingGameEnded()
        {
            (LoopbackHost loopbackHost, LoopbackClient loopbackClient) = WireLoopback();
            _ = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);
            var clientVm = new LobbyViewModel_Client("Client", loopbackClient, "");
            await AwaitJoin(clientVm);

            int endedCount = 0;
            clientVm.OnGameEnded += _ => endedCount++;

            clientVm.Dispose();
            clientVm.Dispose(); //Idempotent, as on the host side.

            Assert.That(loopbackClient.DisconnectCount, Is.EqualTo(1),
                "Disposing the client lobby must close the connection (so the host frees the roster slot), exactly once.");
            Assert.That(endedCount, Is.Zero,
                "A deliberate teardown must not surface as a 'connection to the host was lost' game-end.");
        }

        [Test]
        public async Task DisposedClientLobby_IgnoresLateBroadcasts()
        {
            (LoopbackHost loopbackHost, LoopbackClient loopbackClient) = WireLoopback();
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);
            var clientVm = new LobbyViewModel_Client("Client", loopbackClient, "");
            await AwaitJoin(clientVm);
            int rosterSizeAtDispose = clientVm.PlayerInfos.Count;

            clientVm.Dispose();

            // The host's roster rebroadcast (here: a local player joining) must not reach a disposed client VM.
            hostVm.AddLocalPlayer();

            Assert.That(clientVm.PlayerInfos.Count, Is.EqualTo(rosterSizeAtDispose),
                "A disposed client lobby must not keep applying roster broadcasts.");
        }

        private static async Task<string?> AwaitJoin(LobbyViewModel_Client client)
        {
            Task<string?> joinTask = client.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "Join handshake did not complete.");
            return await joinTask;
        }

        private static (LoopbackHost host, LoopbackClient client) WireLoopback()
        {
            var loopbackHost = new LoopbackHost();
            var loopbackClient = new LoopbackClient();
            loopbackHost.Client = loopbackClient;
            loopbackClient.Host = loopbackHost;
            return (loopbackHost, loopbackClient);
        }

        // Synchronous in-process loopback doubles, mirroring LobbyJoinGateTests, plus Stop/Disconnect
        // counters for the teardown assertions.
        private static ArraySegment<byte> Copy(ArraySegment<byte> data) =>
            new ArraySegment<byte>(data.ToArray());

        private sealed class LoopbackHost : INetworkHost
        {
            public static readonly ConnectionID ClientId = new ConnectionID(Guid.NewGuid());

            public LoopbackClient? Client;

            public int StopCount;

            public event Action<ConnectionID>? OnNewClientConnected;
            public event Action<ConnectionID>? OnClientDisconnected;
            public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived;

            public Task StartAsync() => Task.CompletedTask;

            public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled)
            {
                Client!.Deliver(Copy(data));
                return Task.CompletedTask;
            }

            public Task SendCommandToSingleClientAsync(ConnectionID clientId, ArraySegment<byte> data, bool isPooled)
            {
                Client!.Deliver(Copy(data));
                return Task.CompletedTask;
            }

            public void DisconnectClient(ConnectionID clientId) { }

            public void MarkClientAuthenticated(ConnectionID clientId) { }

            public void Stop() => StopCount++;

            internal void ReceiveFromClient(ArraySegment<byte> data) =>
                OnMessageReceived?.Invoke(data, ClientId);
        }

        private sealed class LoopbackClient : INetworkClient
        {
            public LoopbackHost? Host;

            public int DisconnectCount;

            public event Action<ArraySegment<byte>>? OnMessageReceived;
            public event Action? OnDisconnected;

            public Task<bool> ConnectAsync(IPAddress serverIP, int port = NetworkProtocol.DefaultPort) => Task.FromResult(true);

            public Task SendCommandToHost(ArraySegment<byte> command, bool isPooled)
            {
                Host!.ReceiveFromClient(Copy(command));
                return Task.CompletedTask;
            }

            public void Disconnect()
            {
                DisconnectCount++;
                OnDisconnected?.Invoke();
            }

            internal void Deliver(ArraySegment<byte> data) => OnMessageReceived?.Invoke(data);
        }
    }
}
