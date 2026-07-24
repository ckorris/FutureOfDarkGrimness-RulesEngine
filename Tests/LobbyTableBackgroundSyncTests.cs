using System;
using System.Net;
using System.Threading.Tasks;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #265 table background sync, end to end through the REAL LobbyViewModel_Host /
    /// LobbyViewModel_Client over the same in-process synchronous loopback as
    /// <see cref="LobbyColorSyncTests"/>: the host's pick lands in its GameSettings, rides the
    /// LobbyGameSettingsUpdate broadcast to the client, and a client may not set it. The value is
    /// cosmetic (the front end maps it to felt colours), but it is a SETTING - so both sides must
    /// agree before launch, and a fresh lobby must start on Forest.
    /// </summary>
    [TestFixture]
    public class LobbyTableBackgroundSyncTests
    {
        [Test]
        public async Task FreshLobby_BothSidesStartOnForest()
        {
            var (host, client) = await StandUpJoinedLobby();

            Assert.That(host.TableBackground, Is.EqualTo(ETableBackground.Forest));
            Assert.That(client.TableBackground, Is.EqualTo(ETableBackground.Forest));
        }

        [Test]
        public async Task HostPick_ReachesTheClient()
        {
            var (host, client) = await StandUpJoinedLobby();

            host.SetTableBackground(ETableBackground.MarsLike);

            Assert.That(host.TableBackground, Is.EqualTo(ETableBackground.MarsLike));
            Assert.That(client.TableBackground, Is.EqualTo(ETableBackground.MarsLike),
                "the settings broadcast carries the pick to the client");
        }

        [Test]
        public async Task HostPick_NotifiesObservers()
        {
            var (host, client) = await StandUpJoinedLobby();

            ETableBackground? seen = null;
            using (client.TableBackgroundObservable.Subscribe(b => seen = b))
            {
                host.SetTableBackground(ETableBackground.Urban);
            }

            Assert.That(seen, Is.EqualTo(ETableBackground.Urban));
        }

        [Test]
        public async Task EveryValue_SurvivesTheWire()
        {
            var (host, client) = await StandUpJoinedLobby();

            foreach (ETableBackground background in Enum.GetValues<ETableBackground>())
            {
                host.SetTableBackground(background);
                Assert.That(client.TableBackground, Is.EqualTo(background), $"{background} did not sync");
            }
        }

        [Test]
        public async Task UndefinedValue_IsRejected()
        {
            var (host, client) = await StandUpJoinedLobby();

            host.SetTableBackground(ETableBackground.Ice);
            host.SetTableBackground((ETableBackground)999);

            Assert.That(host.TableBackground, Is.EqualTo(ETableBackground.Ice), "garbage leaves the pick alone");
            Assert.That(client.TableBackground, Is.EqualTo(ETableBackground.Ice));
        }

        [Test]
        public async Task ClientSet_Throws()
        {
            var (_, client) = await StandUpJoinedLobby();

            Assert.Throws<InvalidOperationException>(() => client.SetTableBackground(ETableBackground.Desert),
                "only the host owns the lobby settings");
        }

        // Wires a host lobby and a client lobby over the loopback and completes the join handshake.
        // Mirrors LobbyColorSyncTests.StandUpJoinedLobby.
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
