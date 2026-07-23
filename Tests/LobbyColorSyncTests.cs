using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #221 lobby colour sync, end to end through the REAL LobbyViewModel_Host / LobbyViewModel_Client over
    /// the same in-process synchronous loopback as <see cref="LobbyJoinGateTests"/>: a pick lands in
    /// LobbyPlayerInfoFull on the host, rides the roster rebroadcast into BOTH sides' PlayerInfos, and a
    /// pick for a colour another player already explicitly holds is ignored (first-committed-wins).
    /// ColorIndex is an opaque int to the engine - the palette lives app-side.
    /// </summary>
    [TestFixture]
    public class LobbyColorSyncTests
    {
        [Test]
        public async Task HostPick_ReachesBothRosters()
        {
            var (host, client) = await StandUpJoinedLobby();
            PlayerID hostPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;

            host.SetPlayerColor(hostPlayer, 5);

            Assert.That(ColorOf(host, hostPlayer), Is.EqualTo(5), "host roster carries the pick");
            Assert.That(ColorOf(client, hostPlayer), Is.EqualTo(5), "rebroadcast carries it to the client");
        }

        [Test]
        public async Task ClientPick_OwnRow_RoundTripsToBothRosters()
        {
            var (host, client) = await StandUpJoinedLobby();
            PlayerID clientPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Client").PlayerID;

            client.SetPlayerColor(clientPlayer, 7);

            Assert.That(ColorOf(host, clientPlayer), Is.EqualTo(7), "host applied the client's pick");
            Assert.That(ColorOf(client, clientPlayer), Is.EqualTo(7), "client sees it back via the rebroadcast");
        }

        [Test]
        public async Task ClientPick_ForAnotherPlayer_Throws()
        {
            var (host, client) = await StandUpJoinedLobby();
            PlayerID hostPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;

            Assert.Throws<InvalidOperationException>(() => client.SetPlayerColor(hostPlayer, 3),
                "a client may only set its own colour");
        }

        [Test]
        public async Task PickingAnAlreadyHeldColour_IsIgnored()
        {
            var (host, client) = await StandUpJoinedLobby();
            PlayerID hostPlayer   = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;
            PlayerID clientPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Client").PlayerID;

            host.SetPlayerColor(hostPlayer, 2);
            client.SetPlayerColor(clientPlayer, 2); // race loser: colour 2 is explicitly held

            Assert.That(ColorOf(host, hostPlayer), Is.EqualTo(2), "first pick holds");
            Assert.That(ColorOf(host, clientPlayer), Is.EqualTo(-1), "conflicting pick ignored, no pick recorded");
            Assert.That(ColorOf(client, clientPlayer), Is.EqualTo(-1), "client roster agrees");
        }

        [Test]
        public async Task RepickingAFreedColour_Succeeds()
        {
            var (host, client) = await StandUpJoinedLobby();
            PlayerID hostPlayer   = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;
            PlayerID clientPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Client").PlayerID;

            host.SetPlayerColor(hostPlayer, 2);
            host.SetPlayerColor(hostPlayer, 4);          // moves off 2
            client.SetPlayerColor(clientPlayer, 2);      // now free

            Assert.That(ColorOf(host, hostPlayer), Is.EqualTo(4));
            Assert.That(ColorOf(host, clientPlayer), Is.EqualTo(2), "freed colour is pickable again");
            Assert.That(ColorOf(client, clientPlayer), Is.EqualTo(2));
        }

        private static int ColorOf(ILobbyViewModel vm, PlayerID player) =>
            vm.PlayerInfos.Single(p => p.PlayerID == player).ColorIndex;

        // Wires a host lobby and a client lobby over the loopback and completes the join handshake, so both
        // rosters hold Host + Client. Mirrors LobbyJoinGateTests.StandUpLobby.
        private static async Task<(LobbyViewModel_Host host, LobbyViewModel_Client client)> StandUpJoinedLobby()
        {
            var loopbackHost = new LoopbackHost();
            var loopbackClient = new LoopbackClient();
            loopbackHost.Client = loopbackClient;
            loopbackClient.Host = loopbackHost;

            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);
            var clientVm = new LobbyViewModel_Client("Client", loopbackClient, "");

            Task<string?> joinTask = clientVm.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "Join handshake did not complete.");
            Assert.That(await joinTask, Is.Null, "open lobby join must be accepted");

            return (hostVm, clientVm);
        }

        // Synchronous in-process loopback doubles, mirroring LobbyJoinGateTests / NetworkedFullStateSyncTests.
        private static ArraySegment<byte> Copy(ArraySegment<byte> data) =>
            new ArraySegment<byte>(data.ToArray());

        private sealed class LoopbackHost : INetworkHost
        {
            public static readonly ConnectionID ClientId = new ConnectionID(Guid.NewGuid());

            public LoopbackClient? Client;

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

            public void Stop() { }

            internal void ReceiveFromClient(ArraySegment<byte> data) =>
                OnMessageReceived?.Invoke(data, ClientId);
        }

        private sealed class LoopbackClient : INetworkClient
        {
            public LoopbackHost? Host;

            public event Action<ArraySegment<byte>>? OnMessageReceived;
            public event Action? OnDisconnected;

            public Task<bool> ConnectAsync(IPAddress serverIP, int port = NetworkProtocol.DefaultPort) => Task.FromResult(true);

            public Task SendCommandToHost(ArraySegment<byte> command, bool isPooled)
            {
                Host!.ReceiveFromClient(Copy(command));
                return Task.CompletedTask;
            }

            public void Disconnect() => OnDisconnected?.Invoke();

            internal void Deliver(ArraySegment<byte> data) => OnMessageReceived?.Invoke(data);
        }
    }
}
