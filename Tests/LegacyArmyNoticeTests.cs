using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.ArmyBuilding;
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
    /// #440 (plan Q5): a Forge army whose embedded book is stamped with an older OPR ruleset gets a one-line,
    /// non-blocking notice that it plays with the current rules. The stamp is read off the embedded book, rides the
    /// lobby roster (so a client sees the notice for the host's and the bots' armies too), and never blocks.
    /// </summary>
    [TestFixture]
    public class LegacyArmyNoticeTests
    {
        private static BuiltArmyFile ForgeArmy(string name, string bookVersion) => new()
        {
            Name = name, Faction = "Demo", PointsLimit = 1000,
            Book = new BookFile { Name = "Demo", Faction = "Demo", Version = bookVersion },
            Selections = new BuilderList { Name = name, BookName = "Demo" },
            Units = { new UnitFileEntry { Name = "Warriors", ModelCount = 5, PointCost = 100 } },
        };

        [TestCase("OPR v3.5.3", true)]
        [TestCase("OPR v3.5.1", true)]
        [TestCase("OPR v3.6.0", false)]
        [TestCase("OPR v3.6.2", false)]
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("custom", false)]
        public void IsLegacy_IsAnyParseableStampThatIsNot36(string? stamp, bool legacy) =>
            Assert.That(LegacyArmyNotice.IsLegacy(stamp), Is.EqualTo(legacy));

        [Test]
        public void Line_NamesTheArmyAndItsRuleset_AndIsAscii()
        {
            string? line = LegacyArmyNotice.Line(ForgeArmy("Bug Swarm", "OPR v3.5.3"));

            Assert.That(line, Is.EqualTo("Army 'Bug Swarm' was built for OPR v3.5.3; it plays with 3.6 rules"));
            Assert.That(line!.All(c => c < 128), Is.True, "game text is ASCII-only");
        }

        [Test]
        public void NoNotice_ForA36Army_OrAHandAuthoredOne()
        {
            Assert.That(LegacyArmyNotice.Line(ForgeArmy("New", "OPR v3.6.0")), Is.Null);
            Assert.That(LegacyArmyNotice.Line(new ArmyListFile { Name = "Hand-made" }), Is.Null,
                "a list with no embedded book carries no stamp, so nothing says it is old");
        }

        [Test]
        public void TheRosterCarriesTheStamp_AcrossTheWire_AndTheLaunchIsNotBlocked()
        {
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            PlayerID host = hostVm.PlayerInfos.Single().PlayerID;
            hostVm.UpdateArmyListFile(host, ForgeArmy("Old Guard", "OPR v3.5.3"));

            ArmyListSummary summary = hostVm.PlayerInfos.Single().ArmyListSummary;
            Assert.That(summary.RulesVersion, Is.EqualTo("OPR v3.5.3"));
            Assert.That(hostVm.BlockingLaunchProblems(), Is.Empty, "the notice is advice, never a blocker");
            Assert.That(hostVm.ValidateArmiesForLaunch().Any(p => p.Contains("3.5")), Is.False);

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var sender = new MessageSerializer(store);
            sender.RegisterMessageType<LobbyPlayerListUpdate>();
            ArraySegment<byte> bytes = sender.SerializeMessage(
                new LobbyPlayerListUpdate(new List<LobbyPlayerInfoSummary>(hostVm.PlayerInfos)));
            var receiver = new MessageSerializer(store);
            receiver.RegisterMessageType<LobbyPlayerListUpdate>();

            var decoded = (LobbyPlayerListUpdate)receiver.DeserializeMessage(bytes)!;
            ArmyListSummary onClient = decoded.PlayerInfoList.Single().ArmyListSummary;
            Assert.That(LegacyArmyNotice.Line(onClient.ArmyName, onClient.RulesVersion),
                Is.EqualTo("Army 'Old Guard' was built for OPR v3.5.3; it plays with 3.6 rules"));
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
