using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #406: the launch message carries a host-minted game ID so the front end's anonymous usage events
    /// (host game_start/game_end, client game_join) can be paired. The field is optional on the wire in
    /// both directions - an older host sends none, an older client ignores it - which is why it needed
    /// no protocol bump. Also pins the host-only AI-profile lookup the host's game_start reads.
    /// </summary>
    [TestFixture]
    public class LaunchGameIdTests
    {
        private static MessageSerializer Serializer()
        {
            var serializer = new MessageSerializer(GameDataStore.GameDataStoreBuilder.GetDefault());
            serializer.RegisterMessageType<LaunchGameMessage>();
            return serializer;
        }

        // The wire frame by hand: [4-byte type length][type string][JSON] - what a host of any version sends.
        private static ArraySegment<byte> RawFrame(string json)
        {
            byte[] type = Encoding.UTF8.GetBytes(typeof(LaunchGameMessage).ToString());
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[sizeof(int) + type.Length + body.Length];
            BitConverter.TryWriteBytes(frame.AsSpan(0, sizeof(int)), type.Length);
            type.CopyTo(frame, sizeof(int));
            body.CopyTo(frame, sizeof(int) + type.Length);
            return new ArraySegment<byte>(frame);
        }

        [Test]
        public void GameId_RoundTripsAcrossTheWire()
        {
            const string id = "2b3c4d5e-6f70-4b1c-8d2e-4f5a6b7c8d9e";
            ArraySegment<byte> bytes = Serializer().SerializeMessage(new LaunchGameMessage { GameId = id });

            object? decoded = Serializer().DeserializeMessage(bytes);

            Assert.That(decoded, Is.InstanceOf<LaunchGameMessage>());
            Assert.That(((LaunchGameMessage)decoded!).GameId, Is.EqualTo(id));
        }

        [Test]
        public void AnOlderHostsEmptyLaunchMessage_DecodesWithNoId()
        {
            object? decoded = Serializer().DeserializeMessage(RawFrame("{}"));

            Assert.That(decoded, Is.InstanceOf<LaunchGameMessage>());
            Assert.That(((LaunchGameMessage)decoded!).GameId, Is.Null);
        }

        [Test]
        public void UnknownMembersFromANewerPeer_AreIgnored()
        {
            // The reverse skew: what an older client sees from a newer host is "a member it doesn't
            // know", which must decode rather than drop the launch.
            object? decoded = Serializer().DeserializeMessage(
                RawFrame("{\"GameId\":\"2b3c4d5e-6f70-4b1c-8d2e-4f5a6b7c8d9e\",\"SomeFutureField\":42}"));

            Assert.That(decoded, Is.InstanceOf<LaunchGameMessage>());
        }

        [Test]
        public void LaunchedGameId_IsNullBeforeLaunch()
        {
            var host = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            Assert.That(((ILobbyViewModel)host).LaunchedGameId, Is.Null);
        }

        [Test]
        public void Host_ReportsEachBotsAiProfile_AndNullForHumans()
        {
            var host = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            host.AddAiPlayer(EAiProfile.Tactician);
            host.AddAiPlayer(EAiProfile.Strategist);

            var roster = host.PlayerInfos;
            PlayerID human = roster.Single(p => p.PlayerType == EPlayerType.Local).PlayerID;
            PlayerID[] bots = roster.Where(p => p.PlayerType == EPlayerType.AI).Select(p => p.PlayerID).ToArray();

            Assert.That(host.GetAiProfile(human), Is.Null);
            Assert.That(bots.Select(host.GetAiProfile), Is.EqualTo(new EAiProfile?[] { EAiProfile.Tactician, EAiProfile.Strategist }));
        }
        // Host-only no-op network double (same as LobbyBotNamingTests'): nothing here launches or joins.
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
