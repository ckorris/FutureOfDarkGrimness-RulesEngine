using System;
using FDG.Data;
using FDG.Network.Messages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #073 — bus dispatch hardening (audit §6 / §13.15-17). Two failure modes that used to fail silently
    // or fatally at the network boundary:
    //   (1) a throwing message handler propagated out of dispatch into the read loop's catch-all and
    //       disconnected the client — one stray message could drop a player.
    //   (2) an unknown wire type (version skew) vanished via Debug.WriteLine, compiled out of Release.
    [TestFixture]
    public class BusDispatchHardeningTests
    {
        private sealed record HandledMessage(string Value);
        private sealed record OtherMessage(string Value);

        [Test]
        public void DispatchToHandlers_OneThrowingHandler_DoesNotAbortRemainingHandlers()
        {
            var log = new RecordingTextOutput();
            var registrar = new MessageRegistrar(log);

            bool secondHandlerRan = false;
            registrar.RegisterForMessageEvent<HandledMessage>(_ => throw new InvalidOperationException("boom"));
            registrar.RegisterForMessageEvent<HandledMessage>(_ => secondHandlerRan = true);

            // Must not throw — the exception is isolated, not bubbled into the caller (the read loop).
            Assert.DoesNotThrow(() => registrar.DispatchToHandlers(new HandledMessage("x")));

            Assert.That(secondHandlerRan, Is.True, "a throwing handler must not prevent later handlers from running");
            Assert.That(log.Entries, Has.Count.EqualTo(1), "the failure should be logged, not swallowed");
            // The real exception (InvalidOperationException), not DynamicInvoke's TargetInvocationException wrapper.
            Assert.That(log.Entries[0].Message, Does.Contain(nameof(InvalidOperationException)));
            Assert.That(log.Entries[0].Message, Does.Contain("boom"));
        }

        [Test]
        public void DispatchToHandlers_UnregisteredType_IsNoOp()
        {
            var log = new RecordingTextOutput();
            var registrar = new MessageRegistrar(log);

            Assert.DoesNotThrow(() => registrar.DispatchToHandlers(new OtherMessage("x")));
            Assert.That(log.Entries, Is.Empty);
        }

        [Test]
        public void DeserializeMessage_UnknownType_WarnsOncePerTypeAndReturnsNull()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var log = new RecordingTextOutput();

            // A serializer that knows OtherMessage produces bytes a serializer that doesn't know it must tolerate.
            var sender = new MessageSerializer(store);
            sender.RegisterMessageType<OtherMessage>();
            ArraySegment<byte> bytes = sender.SerializeMessage(new OtherMessage("payload"));

            var receiver = new MessageSerializer(store, log); // deliberately does NOT register OtherMessage

            object? first = receiver.DeserializeMessage(bytes);
            object? second = receiver.DeserializeMessage(bytes);

            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(log.Entries, Has.Count.EqualTo(1), "unknown type should warn once, not once per message");
            Assert.That(log.Entries[0].Message, Does.Contain(typeof(OtherMessage).ToString()));
        }
    }
}
