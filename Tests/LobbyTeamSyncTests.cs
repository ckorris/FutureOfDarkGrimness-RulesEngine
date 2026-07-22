using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Data;
using FDG.EngineInterface;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #255 lobby team selection, end to end through the REAL LobbyViewModel_Host / LobbyViewModel_Client
    /// over the same in-process synchronous loopback as <see cref="LobbyColorSyncTests"/>: new players
    /// default to the first empty team (as many teams as players), picks land in LobbyPlayerInfoFull on
    /// the host and ride the roster rebroadcast into BOTH sides' PlayerInfos, out-of-range picks are
    /// ignored, and launch is hard-blocked while all players share one team.
    /// </summary>
    [TestFixture]
    public class LobbyTeamSyncTests
    {
        [Test]
        public async Task NewPlayers_DefaultToFirstEmptyTeam()
        {
            var (host, _) = await StandUpJoinedLobby();

            Assert.That(TeamOf(host, "Host"), Is.EqualTo(ETeamOption.Team1), "host seeds team 1");
            Assert.That(TeamOf(host, "Client"), Is.EqualTo(ETeamOption.Team2), "joiner takes the first empty team");
        }

        [Test]
        public async Task AddedBot_FillsTheGap_WhenALowTeamIsFree()
        {
            var (host, _) = await StandUpJoinedLobby();
            PlayerID hostPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;

            host.SetPlayerTeam(hostPlayer, ETeamOption.Team2); // host joins the client's team; team 1 now empty
            host.AddAiPlayer(EAiProfile.SoloRules);

            Assert.That(TeamOf(host, "DerpBot 1"), Is.EqualTo(ETeamOption.Team1), "bot takes the freed low team");
        }

        [Test]
        public async Task HostPick_ReachesBothRosters()
        {
            var (host, client) = await StandUpJoinedLobby();
            PlayerID hostPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;

            host.SetPlayerTeam(hostPlayer, ETeamOption.Team2);

            Assert.That(TeamOf(host, "Host"), Is.EqualTo(ETeamOption.Team2), "host roster carries the pick");
            Assert.That(TeamOf(client, "Host"), Is.EqualTo(ETeamOption.Team2), "rebroadcast carries it to the client");
        }

        [Test]
        public async Task ClientPick_OwnRow_RoundTripsToBothRosters()
        {
            var (host, client) = await StandUpJoinedLobby();
            PlayerID clientPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Client").PlayerID;

            client.SetPlayerTeam(clientPlayer, ETeamOption.Team1); // joins the host's team

            Assert.That(TeamOf(host, "Client"), Is.EqualTo(ETeamOption.Team1), "host applied the client's pick");
            Assert.That(TeamOf(client, "Client"), Is.EqualTo(ETeamOption.Team1), "client sees it back via the rebroadcast");
        }

        [Test]
        public async Task ClientPick_ForAnotherPlayer_Throws()
        {
            var (host, client) = await StandUpJoinedLobby();
            PlayerID hostPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;

            Assert.Throws<InvalidOperationException>(() => client.SetPlayerTeam(hostPlayer, ETeamOption.Team2),
                "a client may only set its own team");
        }

        [Test]
        public async Task OutOfRangePick_IsIgnored()
        {
            var (host, _) = await StandUpJoinedLobby();
            PlayerID hostPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;

            host.SetPlayerTeam(hostPlayer, ETeamOption.Team3); // only 2 players -> only teams 1..2 exist
            Assert.That(TeamOf(host, "Host"), Is.EqualTo(ETeamOption.Team1), "team above player count ignored");

            host.SetPlayerTeam(hostPlayer, ETeamOption.None);
            Assert.That(TeamOf(host, "Host"), Is.EqualTo(ETeamOption.Team1), "None ignored");
        }

        [Test]
        public async Task Launch_AllPlayersOnOneTeam_IsHardBlocked()
        {
            var (host, _) = await StandUpJoinedLobby();
            PlayerID hostPlayer = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;

            host.SetPlayerTeam(hostPlayer, ETeamOption.Team2); // both players now on team 2

            bool started = host.TryLaunchGame(out string? failReason);

            Assert.That(started, Is.False, "launch must be blocked");
            Assert.That(failReason, Does.Contain("same team"));
        }

        [Test]
        public async Task Launch_DistinctTeams_PassesTheTeamGate()
        {
            var (host, _) = await StandUpJoinedLobby();

            // Teams differ (1 vs 2). Make the EARLIER terrain check fail so TryLaunchGame returns
            // without actually launching a game: the failure being the terrain message (and not the
            // team message) proves the team gate passed.
            host.SetTerrainPlacementMode(ETerrainPlacementMode.LoadFromFile);

            bool started = host.TryLaunchGame(out string? failReason);

            Assert.That(started, Is.False);
            Assert.That(failReason, Does.Not.Contain("same team"), "team gate must not fire for distinct teams");
            Assert.That(failReason, Does.Contain("layout"), "expected the deliberate terrain failure instead");
        }

        private static ETeamOption TeamOf(ILobbyViewModel vm, string playerName) =>
            vm.PlayerInfos.Single(p => p.PlayerName == playerName).TeamNumber;

        // Wires a host lobby and a client lobby over the loopback and completes the join handshake, so both
        // rosters hold Host + Client. Mirrors LobbyColorSyncTests.StandUpJoinedLobby.
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

        // Synchronous in-process loopback doubles, mirroring LobbyColorSyncTests / LobbyJoinGateTests.
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

            public void Stop() { }

            internal void ReceiveFromClient(ArraySegment<byte> data) =>
                OnMessageReceived?.Invoke(data, ClientId);
        }

        private sealed class LoopbackClient : INetworkClient
        {
            public LoopbackHost? Host;

            public event Action<ArraySegment<byte>>? OnMessageReceived;
            public event Action? OnDisconnected;

            public Task<bool> ConnectAsync(IPAddress serverIP) => Task.FromResult(true);

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
