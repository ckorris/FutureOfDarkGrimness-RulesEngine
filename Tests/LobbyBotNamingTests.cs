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
    /// #217: bots are numbered by their rank among bots of the SAME profile, not by the total player
    /// count at add-time - the first Tactician Bot added after a human host is "Tactician Bot 1", and
    /// the Tactician / DerpBot counters run independently of each other and of human players.
    /// </summary>
    [TestFixture]
    public class LobbyBotNamingTests
    {
        [Test]
        public void BotNames_NumberPerProfile_NotByPlayerCount()
        {
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());

            hostVm.AddAiPlayer(EAiProfile.Tactician);
            hostVm.AddAiPlayer(EAiProfile.SoloRules);
            hostVm.AddAiPlayer(EAiProfile.Tactician);

            string[] botNames = hostVm.PlayerInfos
                .Where(info => info.PlayerType == EPlayerType.AI)
                .Select(info => info.PlayerName)
                .ToArray();

            Assert.That(botNames, Is.EqualTo(new[] { "Tactician Bot 1", "DerpBot 1", "Tactician Bot 2" }),
                "Bot numbering must count same-profile bots only (host is player 1, yet the first " +
                "Tactician is still 'Tactician Bot 1'; the DerpBot between them must not bump it to 3).");
        }

        // Host-only no-op network double: this test never launches or joins anything, it only exercises
        // roster naming, so broadcasts go nowhere. (The loopback doubles in LobbyJoinGateTests need a
        // wired client; here there is none.)
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
            public void Stop() { }
        }
    }
}
