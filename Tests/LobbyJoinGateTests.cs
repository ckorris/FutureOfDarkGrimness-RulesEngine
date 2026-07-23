using System;
using System.Net;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Exercises the lobby join handshake (QF1 password gate, QF5 targeted PlayerID assignment) end to end
    /// through the REAL LobbyViewModel_Host / LobbyViewModel_Client and their networked message buses, over
    /// an in-process synchronous loopback "network" (no TCP, no threads). The greeting the client sends in
    /// its constructor drives the whole round-trip inline, so by the time construction returns the host has
    /// either accepted (assigned a PlayerID) or rejected the join, and JoinResultTask reflects the outcome.
    /// </summary>
    [TestFixture]
    public class LobbyJoinGateTests
    {
        [Test]
        public async Task WrongPassword_IsRejectedWithReason()
        {
            (LobbyViewModel_Host _, LobbyViewModel_Client client) = StandUpLobby(hostPassword: "swordfish",
                clientPassword: "hunter2");

            string? rejectReason = await AwaitJoin(client);

            Assert.That(rejectReason, Is.EqualTo("Incorrect password."),
                "A client whose greeting password doesn't match the host's must be rejected with a reason.");
        }

        [Test]
        public async Task CorrectPassword_IsAccepted()
        {
            (LobbyViewModel_Host _, LobbyViewModel_Client client) = StandUpLobby(hostPassword: "swordfish",
                clientPassword: "swordfish");

            string? rejectReason = await AwaitJoin(client);

            // A null result is the accept signal: the host's PlayerID assignment (sent only to this
            // connection - QF5) reached the client and completed the join.
            Assert.That(rejectReason, Is.Null, "A matching password must be accepted (null = joined).");
        }

        [Test]
        public async Task OpenLobby_AcceptsAnyPassword()
        {
            (LobbyViewModel_Host _, LobbyViewModel_Client client) = StandUpLobby(hostPassword: "",
                clientPassword: "whatever the client typed");

            string? rejectReason = await AwaitJoin(client);

            Assert.That(rejectReason, Is.Null,
                "An open lobby (no host password) must not gate on the client's password field.");
        }

        [Test]
        public async Task LostHostConnection_EndsTheGameOnce()
        {
            var loopbackHost = new LoopbackHost();
            var loopbackClient = new LoopbackClient();
            loopbackHost.Client = loopbackClient;
            loopbackClient.Host = loopbackHost;

            _ = new LobbyViewModel_Host("Host", "The Table", "", loopbackHost);
            var clientVm = new LobbyViewModel_Client("Client", loopbackClient, "");
            await AwaitJoin(clientVm); // join accepted (open lobby)

            int endedCount = 0;
            string? endResult = null;
            clientVm.OnGameEnded += result => { endedCount++; endResult = result; };

            // Host vanished: the client's connection drops.
            loopbackClient.Disconnect();
            // A late GameEndedMessage (or a second disconnect) must not fire the return-to-menu flow again.
            loopbackClient.Disconnect();

            Assert.That(endedCount, Is.EqualTo(1), "A lost host connection must end the game exactly once.");
            Assert.That(endResult, Is.EqualTo("Connection to the host was lost."));
        }

        private static async Task<string?> AwaitJoin(LobbyViewModel_Client client)
        {
            Task<string?> joinTask = client.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "Join handshake did not complete.");
            return await joinTask;
        }

        // Wires a host lobby and a client lobby to each other over the loopback and lets the client's
        // constructor-time greeting run the handshake. Client set on the host before the host VM is built,
        // since the host broadcasts a roster update from its constructor.
        private static (LobbyViewModel_Host host, LobbyViewModel_Client client) StandUpLobby(
            string? hostPassword, string? clientPassword)
        {
            var loopbackHost = new LoopbackHost();
            var loopbackClient = new LoopbackClient();
            loopbackHost.Client = loopbackClient;
            loopbackClient.Host = loopbackHost;

            var hostVm = new LobbyViewModel_Host("Host", "The Table", hostPassword, loopbackHost);
            var clientVm = new LobbyViewModel_Client("Client", loopbackClient, clientPassword);
            return (hostVm, clientVm);
        }

        // Synchronous in-process loopback: each serialized frame is copied (the sender returns its pooled
        // buffer) and handed straight to the other side's receive event. Mirrors the doubles in
        // NetworkedFullStateSyncTests.
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

            public void MarkClientAuthenticated(ConnectionID clientId) { }

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
