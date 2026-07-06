using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.Players;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace FDG.Tests
{
    // #077 — in-game chat from network players: the host-side NetworkPlayerController relays a submitted
    // chat message (via OnMessageSentByPlayer, which LogAndChatMessageRelayer broadcasts to everyone),
    // attributed to the sender and de-duplicated across controllers.
    [TestFixture]
    public class InGameChatTests
    {
        [Test]
        public void ChatMessageForThisPlayer_RaisesOnMessageSentByPlayer()
        {
            var bus = new RequestSystemTests.MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());
            var controller = new NetworkPlayerController("Net", playerID, ConnectionID.Host, bus,
                GameDataStore.GameDataStoreBuilder.GetDefault());

            var received = new List<(PlayerID id, EChatMessageType type, string msg)>();
            controller.OnMessageSentByPlayer += (id, type, msg) => received.Add((id, type, msg));

            bus.SimulateMessageReceived(new NetworkPlayerSubmitChatMessage(playerID, EChatMessageType.Global, "gg"));

            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0].id, Is.EqualTo(playerID));
            Assert.That(received[0].type, Is.EqualTo(EChatMessageType.Global));
            Assert.That(received[0].msg, Is.EqualTo("gg"));
        }

        // The bug a blind uncomment would have shipped: every network player's controller is registered for
        // the message, so a controller must ignore chat from other players or the same line would be
        // re-raised (and re-broadcast) once per network player, misattributed.
        [Test]
        public void ChatMessageForAnotherPlayer_IsIgnored()
        {
            var bus = new RequestSystemTests.MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());
            var controller = new NetworkPlayerController("Net", playerID, ConnectionID.Host, bus,
                GameDataStore.GameDataStoreBuilder.GetDefault());

            int fireCount = 0;
            controller.OnMessageSentByPlayer += (_, _, _) => fireCount++;

            bus.SimulateMessageReceived(new NetworkPlayerSubmitChatMessage(
                new PlayerID(Guid.NewGuid()), EChatMessageType.Global, "not mine"));

            Assert.That(fireCount, Is.EqualTo(0),
                "a controller relays only its own player's chat, so there's no misattributed re-broadcast.");
        }

        // #105 de-dup: a controller represents ONE client, so its outgoing chat targets that client's
        // connection (SendCommandToSingleAsync), NOT a broadcast (SendCommandToAllAsync). A broadcast also
        // dispatches locally on the host, so the host would display every line twice (its own
        // LocalPlayerController plus the loopback).
        [Test]
        public void SendPlayerMessage_TargetsThisClientsConnection_NotABroadcast()
        {
            var bus = new RequestSystemTests.MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());
            var controller = new NetworkPlayerController("Net", playerID, ConnectionID.Host, bus,
                GameDataStore.GameDataStoreBuilder.GetDefault());

            controller.SendPlayerMessage("Host", EChatMessageType.Global, "hi");

            Assert.That(bus.LastSentWasBroadcast, Is.False,
                "per-client send avoids the host-loopback duplicate.");
            Assert.That(bus.LastSentConnection, Is.EqualTo(ConnectionID.Host));
            Assert.That(bus.LastSentCommand, Is.InstanceOf<PlayerChatNetworkMessage>());
        }
    }
}
