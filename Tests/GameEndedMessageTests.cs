using FDG.Data;
using FDG.Network.Messages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #040 — networked-client post-game return. A non-host client has no FDGServer, so it learns the
    // game ended from a GameEndedMessage the host broadcasts. These pin that wire contract: the result
    // string survives the serialize → deserialize round-trip and reaches a registered handler intact.
    [TestFixture]
    public class GameEndedMessageTests
    {
        [Test]
        public void GameEndedMessage_RoundTripsResultStringAcrossTheWire()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();

            var sender = new MessageSerializer(store);
            sender.RegisterMessageType<GameEndedMessage>();
            System.ArraySegment<byte> bytes = sender.SerializeMessage(new GameEndedMessage("Player 7 wins!"));

            // The receiver must register the type to recognize it (the client does this in its ctor).
            var receiver = new MessageSerializer(store);
            receiver.RegisterMessageType<GameEndedMessage>();

            object? decoded = receiver.DeserializeMessage(bytes);

            Assert.That(decoded, Is.InstanceOf<GameEndedMessage>());
            Assert.That(((GameEndedMessage)decoded!).Result, Is.EqualTo("Player 7 wins!"));
        }

        [Test]
        public void GameEndedMessage_DispatchesResultToRegisteredHandler()
        {
            var registrar = new MessageRegistrar();

            string? received = null;
            registrar.RegisterForMessageEvent<GameEndedMessage>(m => received = m.Result);

            registrar.DispatchToHandlers(new GameEndedMessage("It's a tie!"));

            Assert.That(received, Is.EqualTo("It's a tie!"));
        }
    }
}
