using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FDG.Network.Connection;
using FDG.Network.Messages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #074 — connection-aware dispatch (audit §6). The source ConnectionID is threaded through
    // DispatchToHandlers as a parameter instead of read from ambient state on the bus. With one read
    // loop per connected client, the old ambient field could be overwritten by a concurrent dispatch,
    // so a handler answering RequestAllData / the lobby greeting could reply to the WRONG client.
    [TestFixture]
    public class ConnectionMessageDispatchTests
    {
        private sealed record TaggedMessage(Guid ExpectedConnection);
        private sealed record PlainMessage(string Value);

        [Test]
        public void ConnectionAwareHandler_ReceivesDispatchedConnectionId()
        {
            var registrar = new MessageRegistrar();
            var expected = new ConnectionID(Guid.NewGuid());
            ConnectionID? seen = null;

            registrar.RegisterForConnectionMessageEvent<PlainMessage>((_, conn) => seen = conn);
            registrar.DispatchToHandlers(new PlainMessage("x"), expected);

            Assert.That(seen, Is.EqualTo(expected));
        }

        [Test]
        public void PlainAndConnectionAwareHandlers_OnSameType_BothFire()
        {
            var registrar = new MessageRegistrar();
            bool plainRan = false;
            bool connRan = false;

            registrar.RegisterForMessageEvent<PlainMessage>(_ => plainRan = true);
            registrar.RegisterForConnectionMessageEvent<PlainMessage>((_, __) => connRan = true);

            registrar.DispatchToHandlers(new PlainMessage("x"), new ConnectionID(Guid.NewGuid()));

            Assert.That(plainRan, Is.True);
            Assert.That(connRan, Is.True);
        }

        [Test]
        public void LocalDispatch_NoConnection_SkipsConnectionAwareHandlerButRunsPlain()
        {
            var registrar = new MessageRegistrar();
            bool plainRan = false;
            bool connRan = false;

            registrar.RegisterForMessageEvent<PlainMessage>(_ => plainRan = true);
            registrar.RegisterForConnectionMessageEvent<PlainMessage>((_, __) => connRan = true);

            // No connection id (locally-originated message, e.g. SendCommandToAll dispatching to the local player).
            registrar.DispatchToHandlers(new PlainMessage("x"));

            Assert.That(plainRan, Is.True, "plain handlers still run for local dispatch");
            Assert.That(connRan, Is.False, "connection-aware handlers need a real connection and are skipped locally");
        }

        [Test]
        public void Deregister_RemovesConnectionAwareHandler()
        {
            var registrar = new MessageRegistrar();
            int calls = 0;
            Action<PlainMessage, ConnectionID> handler = (_, __) => calls++;

            registrar.RegisterForConnectionMessageEvent(handler);
            registrar.DeregisterForConnectionMessageEvent(handler);
            registrar.DispatchToHandlers(new PlainMessage("x"), new ConnectionID(Guid.NewGuid()));

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void ConcurrentDispatches_EachHandlerSeesItsOwnConnectionId()
        {
            // The regression test for the race: many connections dispatch concurrently, each message
            // carrying the id it was dispatched with. Threading the id through the call (vs ambient state)
            // means a handler can never observe a different connection's id.
            var registrar = new MessageRegistrar();
            var mismatches = new ConcurrentBag<string>();

            registrar.RegisterForConnectionMessageEvent<TaggedMessage>((msg, conn) =>
            {
                if (conn.ID != msg.ExpectedConnection)
                    mismatches.Add($"expected {msg.ExpectedConnection}, saw {conn.ID}");
            });

            Parallel.For(0, 5000, i =>
            {
                var id = Guid.NewGuid();
                registrar.DispatchToHandlers(new TaggedMessage(id), new ConnectionID(id));
            });

            Assert.That(mismatches, Is.Empty, "no handler should ever see another connection's id");
        }
    }
}
