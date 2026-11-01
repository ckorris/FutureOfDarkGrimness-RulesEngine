using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #441 F1 (signed off B, 2026-10-31): the lobby shows one amber advisory line per army over its force-org caps.
    /// The host computes the cap report against the LOBBY's points limit (<see cref="ForceOrgValidator.CompositionWarnings"/>),
    /// it rides the roster summary like the #440 ruleset stamp so every machine shows the same line, it follows the
    /// points limit when the host changes it, and it never blocks the launch.
    /// </summary>
    [TestFixture]
    public class LobbyForceOrgNoticeTests
    {
        private static UnitFileEntry Unit(string name, int points, bool hero = false)
        {
            UnitFileEntry unit = new UnitFileEntry { Name = name, ModelCount = hero ? 1 : 5, Quality = 4, Defense = 4, PointCost = points };
            if (hero) unit.SpecialRules.Add(new SpecialRuleEntry_Core("Hero"));
            return unit;
        }

        /// <summary>Three Heroes: legal at 1500+ (1 per 500), one over at 1000.</summary>
        private static ArmyListFile ThreeHeroes() => new ArmyListFile
        {
            Name = "Brass Council", Faction = "Demo", PointsLimit = 1000,
            Units = { Unit("Warriors", 100), Unit("Captain", 100, hero: true), Unit("Lieutenant", 100, hero: true), Unit("Sergeant", 100, hero: true) },
        };

        [Test]
        public void TheRosterCarriesTheCapReport_AtTheLobbysLimit_AndFollowsIt()
        {
            using var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            PlayerID host = hostVm.PlayerInfos.Single().PlayerID;
            Assert.That(hostVm.PlayerInfos.Single().ArmyListSummary.ForceOrgWarnings, Is.Null, "no army, no report");

            hostVm.SetArmyPoints(1000);
            hostVm.UpdateArmyListFile(host, ThreeHeroes());
            Assert.That(hostVm.PlayerInfos.Single().ArmyListSummary.ForceOrgWarnings,
                Is.EqualTo(new[] { "Too many Heroes: 3 (max 2 at 1000 pts; 1 per 500)." }));
            Assert.That(hostVm.BlockingLaunchProblems(), Is.Empty, "advice, never a blocker");

            hostVm.SetArmyPoints(2000);
            Assert.That(hostVm.PlayerInfos.Single().ArmyListSummary.ForceOrgWarnings, Is.Empty,
                "raising the lobby's limit re-syncs the roster: three Heroes are fine at 2000");
        }

        [Test]
        public void TheCapReportSurvivesTheWire()
        {
            using var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            hostVm.SetArmyPoints(1000);
            hostVm.UpdateArmyListFile(hostVm.PlayerInfos.Single().PlayerID, ThreeHeroes());

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var sender = new MessageSerializer(store);
            sender.RegisterMessageType<LobbyPlayerListUpdate>();
            ArraySegment<byte> bytes = sender.SerializeMessage(
                new LobbyPlayerListUpdate(new List<LobbyPlayerInfoSummary>(hostVm.PlayerInfos)));
            var receiver = new MessageSerializer(store);
            receiver.RegisterMessageType<LobbyPlayerListUpdate>();

            var decoded = (LobbyPlayerListUpdate)receiver.DeserializeMessage(bytes)!;
            ArmyListSummary onClient = decoded.PlayerInfoList.Single().ArmyListSummary;
            Assert.That(onClient.ForceOrgWarnings, Is.EqualTo(new[] { "Too many Heroes: 3 (max 2 at 1000 pts; 1 per 500)." }));
        }

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
    }
}
