using System;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #405: the lobby could add local players and bots but never drop one - a misclicked bot was stuck
    /// there until the host tore the whole lobby down. <see cref="ILobbyViewModel.RemovePlayer"/> is the
    /// counterpart, and <see cref="ILobbyViewModel.CheckCanRemovePlayer"/> owns the policy so the front
    /// end never re-derives it.
    ///
    /// <para>The load-bearing case is <see cref="Host_CannotRemoveItself"/>. The host's own slot is
    /// EPlayerType.Local with an empty ConnectionID - byte for byte what an added local player looks
    /// like - so before this work nothing could tell them apart, and the obvious "remove any Local row"
    /// rule would have let the host delete itself out of its own lobby.</para>
    /// </summary>
    [TestFixture]
    public class LobbyPlayerRemovalTests
    {
        private static PlayerID HostOwnSlot(LobbyViewModel_Host host) => host.PlayerInfos[0].PlayerID;

        [Test]
        public void RemovePlayer_DropsTheNamedBot_AndLeavesTheOthers()
        {
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            hostVm.AddAiPlayer(EAiProfile.Tactician);
            hostVm.AddAiPlayer(EAiProfile.Strategist);
            hostVm.AddAiPlayer(EAiProfile.SoloRules);

            PlayerID strategist = hostVm.PlayerInfos.Single(i => i.PlayerName == "Strategist Bot 1").PlayerID;
            Assert.That(hostVm.CheckCanRemovePlayer(strategist), Is.True);

            hostVm.RemovePlayer(strategist);

            Assert.That(hostVm.PlayerInfos.Select(i => i.PlayerName),
                Is.EqualTo(new[] { "Host", "Tactician Bot 1", "DerpBot 1" }),
                "only the named bot may go, and the surviving rows keep their order.");
        }

        [Test]
        public void RemovePlayer_DropsAnAddedLocalPlayer()
        {
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            hostVm.AddLocalPlayer();

            PlayerID localPlayer = hostVm.PlayerInfos.Single(i => i.PlayerName != "Host").PlayerID;
            Assert.That(hostVm.CheckCanRemovePlayer(localPlayer), Is.True,
                "a local player the host added is removable - it is only the host's OWN row that is not.");

            hostVm.RemovePlayer(localPlayer);

            Assert.That(hostVm.PlayerInfos.Count, Is.EqualTo(1));
            Assert.That(hostVm.PlayerInfos[0].PlayerName, Is.EqualTo("Host"));
        }

        /// <summary>
        /// The reason <c>_thisPlayerID</c> had to become a field. Both rows below are Local with an empty
        /// ConnectionID; only the recorded id separates them.
        /// </summary>
        [Test]
        public void Host_CannotRemoveItself()
        {
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            hostVm.AddLocalPlayer();

            PlayerID hostSlot = HostOwnSlot(hostVm);
            LobbyPlayerInfoSummary addedLocal = hostVm.PlayerInfos.Single(i => i.PlayerID != hostSlot);

            Assert.That(hostVm.PlayerInfos.Single(i => i.PlayerID == hostSlot).PlayerType,
                Is.EqualTo(addedLocal.PlayerType),
                "precondition: the host's row is indistinguishable from an added local by type alone.");

            Assert.That(hostVm.CheckCanRemovePlayer(hostSlot), Is.False);

            hostVm.RemovePlayer(hostSlot);

            Assert.That(hostVm.PlayerInfos.Any(i => i.PlayerID == hostSlot), Is.True,
                "RemovePlayer must re-check the policy itself - a stale click cannot delete the host.");
            Assert.That(hostVm.PlayerInfos.Count, Is.EqualTo(2));
        }

        [Test]
        public void CheckCanRemovePlayer_IsFalseForAnUnknownID()
        {
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());

            Assert.That(hostVm.CheckCanRemovePlayer(new PlayerID(Guid.NewGuid())), Is.False);
            Assert.That(hostVm.PlayerInfos.Count, Is.EqualTo(1), "and querying must not disturb the roster.");
        }

        /// <summary>A connected client leaves by disconnecting; removing its slot would be a kick, which
        /// needs its own wire message and is deliberately not built here.</summary>
        [Test]
        public async Task ConnectedClient_IsNotRemovable()
        {
            var net = new LoopbackNetworkHost();
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", net);
            var clientVm = new LobbyViewModel_Client("Alpha", net.Connect("Alpha"), "");

            Task<string?> joinTask = clientVm.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "client never completed the join handshake.");
            Assert.That(await joinTask, Is.Null);

            LobbyPlayerInfoSummary networked = hostVm.PlayerInfos.Single(i => i.PlayerType == EPlayerType.Network);
            Assert.That(hostVm.CheckCanRemovePlayer(networked.PlayerID), Is.False);

            hostVm.RemovePlayer(networked.PlayerID);
            Assert.That(hostVm.PlayerInfos.Any(i => i.PlayerID == networked.PlayerID), Is.True);

            // And the client's own view never offers Remove for anyone, including itself.
            foreach (LobbyPlayerInfoSummary info in hostVm.PlayerInfos)
                Assert.That(clientVm.CheckCanRemovePlayer(info.PlayerID), Is.False,
                    "the roster belongs to the host; a client offers Remove on no row at all.");
        }

        /// <summary>Removal rides the ordinary total roster broadcast, so a client must SEE it.</summary>
        [Test]
        public async Task RemovingABot_ReachesTheConnectedClient()
        {
            var net = new LoopbackNetworkHost();
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", net);
            var clientVm = new LobbyViewModel_Client("Alpha", net.Connect("Alpha"), "");

            Task<string?> joinTask = clientVm.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "client never completed the join handshake.");
            Assert.That(await joinTask, Is.Null);

            hostVm.AddAiPlayer(EAiProfile.Tactician);
            await WaitFor(() => clientVm.PlayerInfos.Any(i => i.PlayerName == "Tactician Bot 1"),
                "client never saw the bot arrive.");

            PlayerID bot = hostVm.PlayerInfos.Single(i => i.PlayerName == "Tactician Bot 1").PlayerID;
            hostVm.RemovePlayer(bot);

            await WaitFor(() => clientVm.PlayerInfos.Any(i => i.PlayerID == bot) == false,
                "client still shows a bot the host removed.");
        }

        // Host-only no-op network double, same shape as the one in LobbyBotNamingTests: the non-networked
        // cases below never join anything, so broadcasts go nowhere. The two networked tests use the real
        // LoopbackNetworkHost instead, because they assert on what a client receives.
        private sealed class NullNetworkHost : INetworkHost
        {
            public event Action<ConnectionID>? OnNewClientConnected { add { } remove { } }
            public event Action<ConnectionID>? OnClientDisconnected { add { } remove { } }
            public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived { add { } remove { } }

            public Task StartAsync() => Task.CompletedTask;
            public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled) => Task.CompletedTask;
            public Task SendCommandToSingleClientAsync(ConnectionID clientId, ArraySegment<byte> data, bool isPooled)
                => Task.CompletedTask;
            public void DisconnectClient(ConnectionID clientId) { }

            public void MarkClientAuthenticated(ConnectionID clientId) { }
            public void Stop() { }
        }

        private static async Task WaitFor(Func<bool> condition, string message)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return;
                await Task.Delay(25);
            }

            Assert.Fail(message);
        }
    }
}
