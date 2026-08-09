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
            (LoopbackNetworkHost loopbackHost, LoopbackNetworkClient loopbackClient) = WireLoopback();
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
            (LoopbackNetworkHost loopbackHost, LoopbackNetworkClient _) = WireLoopback();
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);

            hostVm.Dispose();
            hostVm.Dispose(); //Reached from several app paths, so it must be idempotent.

            Assert.That(loopbackHost.StopCount, Is.EqualTo(1),
                "Disposing the host lobby must stop the listener (releasing the port), exactly once.");
        }

        [Test]
        public async Task ClientDispose_ClosesTheConnection_WithoutRaisingGameEnded()
        {
            (LoopbackNetworkHost loopbackHost, LoopbackNetworkClient loopbackClient) = WireLoopback();
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
            (LoopbackNetworkHost loopbackHost, LoopbackNetworkClient loopbackClient) = WireLoopback();
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

        private static (LoopbackNetworkHost host, LoopbackNetworkClient client) WireLoopback()
        {
            var loopbackHost = new LoopbackNetworkHost();
            var loopbackClient = loopbackHost.Connect("Client");
            return (loopbackHost, loopbackClient);
        }
    }
}
