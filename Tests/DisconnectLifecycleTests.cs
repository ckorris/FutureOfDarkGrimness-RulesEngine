using FDG.Data;
using FDG.GameModel;
using FDG.Network.Connection;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace FDG.Tests
{
    // #076 — disconnect lifecycle: a dropped player's pending decision requests are failed (with a typed
    // PlayerDisconnectedException) instead of hanging the game forever.
    [TestFixture]
    public class DisconnectLifecycleTests
    {
        private static GameDataStore BuildStore() =>
            new GameDataStore.GameDataStoreBuilder().RegisterType<PlayerSlotInfo>(4).Build();

        [Test]
        public void FailPendingRequestsForPlayer_FaultsThatPlayersPendingRequest()
        {
            var store = BuildStore();
            var playerID = new PlayerID(Guid.NewGuid());
            var slot = new PlayerSlot(0, 1, playerID, null, store);
            var bus = new RequestSystemTests.MockMessageBusHost();
            var sender = new RequestMessageSender(bus, store,
                new PlayerSlotManager(new[] { slot }), new EmptyTextOutput());

            Task<bool> pending = sender.RequestDecision<YesNoRequest, bool>(new YesNoRequest(playerID, "Strike back?"));
            Assert.That(pending.IsCompleted, Is.False, "the request is awaiting a reply.");

            sender.FailPendingRequestsForPlayer(playerID, "Player disconnected.");

            Assert.That(pending.IsFaulted, Is.True, "the pending request is faulted, not left hanging.");
            Assert.That(pending.Exception!.InnerException,
                Is.TypeOf<RequestMessageSender.PlayerDisconnectedException>());
        }

        [Test]
        public void FailPendingRequestsForPlayer_LeavesOtherPlayersRequestsPending()
        {
            var store = BuildStore();
            var playerA = new PlayerID(Guid.NewGuid());
            var playerB = new PlayerID(Guid.NewGuid());
            var slotA = new PlayerSlot(0, 1, playerA, null, store);
            var slotB = new PlayerSlot(1, 2, playerB, null, store);
            var bus = new RequestSystemTests.MockMessageBusHost();
            var sender = new RequestMessageSender(bus, store,
                new PlayerSlotManager(new[] { slotA, slotB }), new EmptyTextOutput());

            Task<bool> pendingA = sender.RequestDecision<YesNoRequest, bool>(new YesNoRequest(playerA, "A?"));
            Task<bool> pendingB = sender.RequestDecision<YesNoRequest, bool>(new YesNoRequest(playerB, "B?"));

            sender.FailPendingRequestsForPlayer(playerA, "Player disconnected.");

            Assert.That(pendingA.IsFaulted, Is.True, "the disconnected player's request fails.");
            Assert.That(pendingB.IsCompleted, Is.False, "the other player's request is untouched.");
        }

        // Full path: a network player on a known connection drops, and the request sender (subscribed to the
        // bus's OnClientDisconnected) maps the connection to the player and fails their pending request.
        [Test]
        public void ClientDisconnect_FailsPendingRequestForThatConnection()
        {
            var store = BuildStore();
            var playerID = new PlayerID(Guid.NewGuid());
            var connectionID = new ConnectionID(Guid.NewGuid());
            var slot = new PlayerSlot(0, 1, playerID, null, store);
            var bus = new RequestSystemTests.MockMessageBusHost();
            slot.AssignPlayerController(new NetworkPlayerController("Net", playerID, connectionID, bus, store));

            var sender = new RequestMessageSender(bus, store,
                new PlayerSlotManager(new[] { slot }), new EmptyTextOutput());

            Task<bool> pending = sender.RequestDecision<YesNoRequest, bool>(new YesNoRequest(playerID, "Q?"));

            bus.SimulateClientDisconnected(connectionID);

            Assert.That(pending.IsFaulted, Is.True);
            Assert.That(pending.Exception!.InnerException,
                Is.TypeOf<RequestMessageSender.PlayerDisconnectedException>());
        }

        // The drop that lands while it ISN'T this player's turn: no request is pending to fail, but a later
        // request for that player must fault at once (not hang on the dead connection). Regression for
        // "closed the client and nothing happened, then the host hangs at the client's next turn."
        [Test]
        public void RequestAfterDisconnect_ForThatPlayer_FaultsImmediately()
        {
            var store = BuildStore();
            var playerID = new PlayerID(Guid.NewGuid());
            var connectionID = new ConnectionID(Guid.NewGuid());
            var slot = new PlayerSlot(0, 1, playerID, null, store);
            var bus = new RequestSystemTests.MockMessageBusHost();
            slot.AssignPlayerController(new NetworkPlayerController("Net", playerID, connectionID, bus, store));
            var sender = new RequestMessageSender(bus, store,
                new PlayerSlotManager(new[] { slot }), new EmptyTextOutput());

            // Player drops while nothing is pending for them (it wasn't their turn).
            bus.SimulateClientDisconnected(connectionID);

            // Their turn finally comes up and the engine asks them to decide.
            Task<bool> pending = sender.RequestDecision<YesNoRequest, bool>(new YesNoRequest(playerID, "Q?"));

            Assert.That(pending.IsFaulted, Is.True,
                "a request for an already-disconnected player must fault at once, not hang.");
            Assert.That(pending.Exception!.InnerException,
                Is.TypeOf<RequestMessageSender.PlayerDisconnectedException>());
        }

        // When the mid-game disconnect ends the game, the player-facing message names the departed player
        // and reads like a normal event, not an engine fault / stack trace.
        [Test]
        public void DescribePlayerLeft_NamesTheDepartedPlayer()
        {
            var store = BuildStore();
            var playerID = new PlayerID(Guid.NewGuid());
            var connectionID = new ConnectionID(Guid.NewGuid());
            var slot = new PlayerSlot(0, 1, playerID, null, store);
            var bus = new RequestSystemTests.MockMessageBusHost();
            slot.AssignPlayerController(new NetworkPlayerController("Mrs. Client", playerID, connectionID, bus, store));
            var manager = new PlayerSlotManager(new[] { slot });

            string message = FDGServer.DescribePlayerLeft(manager, playerID);

            Assert.That(message, Does.Contain("Mrs. Client"));
            Assert.That(message, Does.Contain("game has ended"));
            Assert.That(message, Does.Not.Contain("Exception"),
                "a player leaving must not read like an engine fault.");
        }

        // If the departed player's slot is already gone, messaging falls back to a generic label instead of
        // throwing a second exception out of the end-of-game handler.
        [Test]
        public void DescribePlayerLeft_UnknownPlayer_FallsBackGracefully()
        {
            var store = BuildStore();
            var slot = new PlayerSlot(0, 1, new PlayerID(Guid.NewGuid()), null, store);
            var manager = new PlayerSlotManager(new[] { slot });

            string message = FDGServer.DescribePlayerLeft(manager, new PlayerID(Guid.NewGuid()));

            Assert.That(message, Is.EqualTo("A player left the game. The game has ended."));
        }

        // A disconnect for a connection that maps to no game slot must not fail anyone's requests.
        [Test]
        public void ClientDisconnect_UnknownConnection_LeavesRequestsPending()
        {
            var store = BuildStore();
            var playerID = new PlayerID(Guid.NewGuid());
            var connectionID = new ConnectionID(Guid.NewGuid());
            var slot = new PlayerSlot(0, 1, playerID, null, store);
            var bus = new RequestSystemTests.MockMessageBusHost();
            slot.AssignPlayerController(new NetworkPlayerController("Net", playerID, connectionID, bus, store));

            var sender = new RequestMessageSender(bus, store,
                new PlayerSlotManager(new[] { slot }), new EmptyTextOutput());

            Task<bool> pending = sender.RequestDecision<YesNoRequest, bool>(new YesNoRequest(playerID, "Q?"));

            bus.SimulateClientDisconnected(new ConnectionID(Guid.NewGuid())); //A different, unknown connection.

            Assert.That(pending.IsCompleted, Is.False, "an unrelated disconnect leaves the request pending.");
        }
    }
}
