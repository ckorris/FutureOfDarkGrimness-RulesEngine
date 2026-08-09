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
            var loopbackHost = new LoopbackNetworkHost();
            var loopbackClient = loopbackHost.Connect("Client");

            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);
            var clientVm = new LobbyViewModel_Client("Client", loopbackClient, "");

            Task<string?> joinTask = clientVm.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "Join handshake did not complete.");
            Assert.That(await joinTask, Is.Null, "open lobby join must be accepted");

            return (hostVm, clientVm);
        }
    }
}
