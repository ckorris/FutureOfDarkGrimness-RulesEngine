using System;
using System.Collections.Generic;
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
    /// #188 — the multi-remote-client path, over the shared <see cref="LoopbackNetworkHost"/> so every
    /// client has its own ConnectionID and targeted sends reach exactly one of them. Only host + one
    /// remote client had ever been exercised; the single-client doubles these replace could not have
    /// failed any of the assertions below.
    ///
    /// <para>The identity tests are the regression pin QF5 never got. That bug broadcast
    /// <c>LobbyPlayerIDAssignment</c>, and <c>LobbyViewModel_Client</c> adopts an assignment
    /// unconditionally, so each new joiner overwrote every earlier client's <c>_thisPlayerID</c> and a
    /// second remote client silently played as the first.</para>
    /// </summary>
    [TestFixture]
    public class MultiClientLobbyTests
    {
        private static readonly string[] ClientNames = { "Alpha", "Bravo", "Charlie", "Delta" };

        // ── Identity ────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task ThreeClients_EachKeepsItsOwnDistinctPlayerID()
        {
            (LobbyViewModel_Host host, IReadOnlyList<LobbyViewModel_Client> clients, _) = await StandUpLobby(3);

            var claimed = new List<PlayerID>();
            foreach (LobbyViewModel_Client client in clients)
            {
                List<PlayerID> mine = host.PlayerInfos
                    .Select(info => info.PlayerID)
                    .Where(client.CheckCanModifyPlayerIDInfo)
                    .ToList();

                Assert.That(mine.Count, Is.EqualTo(1),
                    "Each client must claim exactly one roster PlayerID as its own.");
                claimed.Add(mine[0]);
            }

            Assert.That(claimed.Distinct().Count(), Is.EqualTo(3),
                "Every client must hold a DIFFERENT PlayerID (QF5: a broadcast assignment made them converge).");
            Assert.That(claimed.Contains(host.PlayerInfos[0].PlayerID), Is.False,
                "No client may claim the host's own slot.");
        }

        [Test]
        public async Task EachClientsIdentity_MatchesItsOwnRosterSlotInJoinOrder()
        {
            (LobbyViewModel_Host host, IReadOnlyList<LobbyViewModel_Client> clients, _) = await StandUpLobby(3);

            // Roster is host first, then clients in join order.
            for (int i = 0; i < clients.Count; i++)
            {
                LobbyPlayerInfoSummary slot = host.PlayerInfos[i + 1];
                Assert.That(slot.PlayerName, Is.EqualTo(ClientNames[i]));
                Assert.That(clients[i].CheckCanModifyPlayerIDInfo(slot.PlayerID), Is.True,
                    $"{ClientNames[i]} must own the roster slot bearing its own name.");
            }
        }

        [Test]
        public async Task EveryClientCanEditItsOwnSlotAndRefusesTheOthers()
        {
            (LobbyViewModel_Host host, IReadOnlyList<LobbyViewModel_Client> clients, _) = await StandUpLobby(3);

            // The client VM throws when asked to edit a PlayerID that isn't its own. Both directions matter,
            // and the first is what catches QF5: under that bug only the NEWEST joiner held a correct
            // identity, so every earlier client refused to edit the very slot it was playing.
            for (int i = 0; i < clients.Count; i++)
            {
                PlayerID ownSlot = host.PlayerInfos[i + 1].PlayerID;
                PlayerID someoneElse = host.PlayerInfos[i == 0 ? 2 : 1].PlayerID;

                Assert.That(() => clients[i].SetPlayerTeam(ownSlot, ETeamOption.Team1),
                    Throws.Nothing, $"{ClientNames[i]} must be able to edit its own slot.");
                Assert.That(() => clients[i].SetPlayerTeam(someoneElse, ETeamOption.Team1),
                    Throws.InstanceOf<InvalidOperationException>(),
                    $"{ClientNames[i]} must refuse to edit another player's slot.");
            }
        }

        [Test]
        public async Task EveryJoiningClient_IsAuthenticatedOnItsOwnConnection()
        {
            (_, _, LoopbackNetworkHost net) = await StandUpLobby(3);

            // #266 lifts the pre-auth frame cap per connection; each joiner must be lifted on its OWN.
            Assert.That(net.AuthenticatedConnections.Distinct().Count(), Is.EqualTo(3));
            foreach (LoopbackNetworkClient client in net.Clients)
                Assert.That(net.AuthenticatedConnections, Does.Contain(client.ConnectionID),
                    $"{client.Label} was never marked authenticated.");
        }

        [Test]
        public async Task RosterCarriesADistinctConnectionPerClient()
        {
            (LobbyViewModel_Host host, _, LoopbackNetworkHost net) = await StandUpLobby(3);

            List<ConnectionID> networkConnections = host.PlayerInfos
                .Where(info => info.PlayerType == EPlayerType.Network)
                .Select(info => info.connectionID)
                .ToList();

            Assert.That(networkConnections.Distinct().Count(), Is.EqualTo(3),
                "Each greeting must be attributed to the connection it actually arrived on.");
            foreach (LoopbackNetworkClient client in net.Clients)
                Assert.That(networkConnections, Does.Contain(client.ConnectionID));
        }

        [Test]
        public void TargetedSend_ReachesExactlyOneClient()
        {
            // Guards the fixture itself: the doubles this replaced ignored the ConnectionID argument, so
            // every routing test above would have passed against a host that broadcast everything.
            var net = new LoopbackNetworkHost();
            LoopbackNetworkClient a = net.Connect("A");
            LoopbackNetworkClient b = net.Connect("B");
            LoopbackNetworkClient c = net.Connect("C");

            net.SendCommandToSingleClientAsync(b.ConnectionID, new ArraySegment<byte>(new byte[] { 1, 2, 3 }), false);

            Assert.That(b.ReceivedFrameCount, Is.EqualTo(1));
            Assert.That(a.ReceivedFrameCount, Is.Zero);
            Assert.That(c.ReceivedFrameCount, Is.Zero);
            Assert.That(net.UnroutableSendCount, Is.Zero);
        }

        [Test]
        public void BroadcastReachesEveryClient()
        {
            var net = new LoopbackNetworkHost();
            LoopbackNetworkClient a = net.Connect("A");
            LoopbackNetworkClient b = net.Connect("B");

            net.SendCommandToAllAsync(new ArraySegment<byte>(new byte[] { 9 }), false);

            Assert.That(a.ReceivedFrameCount, Is.EqualTo(1));
            Assert.That(b.ReceivedFrameCount, Is.EqualTo(1));
        }

        // ── Roster order and teams ──────────────────────────────────────────────────────────

        [Test]
        public async Task RosterOrderIsJoinOrder_OnTheHostAndOnEveryClient()
        {
            (LobbyViewModel_Host host, IReadOnlyList<LobbyViewModel_Client> clients, _) = await StandUpLobby(3);

            string[] expected = { "Host", "Alpha", "Bravo", "Charlie" };
            Assert.That(host.PlayerInfos.Select(p => p.PlayerName), Is.EqualTo(expected));

            foreach (LobbyViewModel_Client client in clients)
                Assert.That(client.PlayerInfos.Select(p => p.PlayerName), Is.EqualTo(expected),
                    "Every client must see the same roster, in the same order, as the host.");
        }

        [Test]
        public async Task FourPlayers_EachDefaultToADistinctTeam()
        {
            (LobbyViewModel_Host host, _, _) = await StandUpLobby(3);

            // #255 FirstEmptyTeam: lowest team 1..N not already held, N counting the arriving player.
            Assert.That(host.PlayerInfos.Select(p => p.TeamNumber), Is.EqualTo(new[]
            {
                ETeamOption.Team1, ETeamOption.Team2, ETeamOption.Team3, ETeamOption.Team4
            }));
        }

        [Test]
        public async Task ATeamPickByOneClient_ReachesTheHostAndEveryOtherClient()
        {
            (LobbyViewModel_Host host, IReadOnlyList<LobbyViewModel_Client> clients, _) = await StandUpLobby(3);

            PlayerID bravoID = host.PlayerInfos[2].PlayerID;
            clients[1].SetPlayerTeam(bravoID, ETeamOption.Team1);

            Assert.That(TeamOf(host, bravoID), Is.EqualTo(ETeamOption.Team1), "host applied the pick");
            foreach (LobbyViewModel_Client client in clients)
                Assert.That(TeamOf(client, bravoID), Is.EqualTo(ETeamOption.Team1),
                    "the roster rebroadcast must carry one client's pick to all the others");
        }

        [Test]
        public async Task AColorPickByOneClient_ReachesEveryOtherClient()
        {
            (LobbyViewModel_Host host, IReadOnlyList<LobbyViewModel_Client> clients, _) = await StandUpLobby(3);

            PlayerID charlieID = host.PlayerInfos[3].PlayerID;
            clients[2].SetPlayerColor(charlieID, 4);

            foreach (LobbyViewModel_Client client in clients)
                Assert.That(client.PlayerInfos.Single(p => p.PlayerID == charlieID).ColorIndex, Is.EqualTo(4));
        }

        [Test]
        public async Task FifthPlayer_DefaultsToATeamOutsideTheDefinedRange()
        {
            // Current engine behavior, pinned rather than endorsed: ETeamOption defines Team1..Team4, but
            // FirstEmptyTeam returns (ETeamOption)(count + 1) unconditionally, so a fifth player defaults
            // to an undefined enum value - which SetPlayerTeam would then reject as out of range if the
            // player tried to re-pick it. Nothing in the engine caps the roster at four. Ruling needed
            // (cap the lobby, clamp the default, or widen ETeamOption) - see WorkItems/188.
            (LobbyViewModel_Host host, _, _) = await StandUpLobby(3);
            host.AddAiPlayer(EAiProfile.SoloRules);

            ETeamOption fifth = host.PlayerInfos[4].TeamNumber;
            Assert.That((int)fifth, Is.EqualTo(5));
            Assert.That(Enum.IsDefined(typeof(ETeamOption), fifth), Is.False,
                "A fifth player's default team is not a defined ETeamOption value.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────

        private static ETeamOption TeamOf(ILobbyViewModel vm, PlayerID player) =>
            vm.PlayerInfos.Single(p => p.PlayerID == player).TeamNumber;

        /// <summary>
        /// Host lobby plus <paramref name="clientCount"/> joined clients. Clients connect AFTER the host VM
        /// exists (unlike the 1v1 helpers, which wire the transport first) so the host observes each
        /// OnNewClientConnected, and each greeting arrives tagged with its own connection.
        /// </summary>
        private static async Task<(LobbyViewModel_Host host, IReadOnlyList<LobbyViewModel_Client> clients,
            LoopbackNetworkHost net)> StandUpLobby(int clientCount)
        {
            var net = new LoopbackNetworkHost();
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", net);

            var clientVms = new List<LobbyViewModel_Client>();
            for (int i = 0; i < clientCount; i++)
            {
                LoopbackNetworkClient transport = net.Connect(ClientNames[i]);
                var clientVm = new LobbyViewModel_Client(ClientNames[i], transport, "");

                Task<string?> joinTask = clientVm.JoinResultTask;
                Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.That(winner, Is.SameAs(joinTask), $"{ClientNames[i]} never completed the join handshake.");
                Assert.That(await joinTask, Is.Null, $"{ClientNames[i]} must be accepted into an open lobby.");

                clientVms.Add(clientVm);
            }

            return (hostVm, clientVms, net);
        }
    }
}
