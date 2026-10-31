using FDG.Data;
using FDG.Network.Messages;
using FDG.Network.Messages.StageRequestMessages;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    // #451: a remote client's own replay recording. Pins structure against the host's recording of the same game,
    // never content (plan rule 8: no golden files).
    [TestFixture]
    public class ClientReplayRecordingTests
    {
        // --- Wire --------------------------------------------------------------------------------------

        [Test]
        public void ResolvedNotice_CarriesTheWholeDecision_AcrossTheWire()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var sender = new MessageSerializer(store);
            var receiver = new MessageSerializer(store);
            receiver.RegisterMessageType<StageTaskNotifyResolvedMessage>();

            var answered = new StageTaskNotifyResolvedMessage(new TaskID(Guid.NewGuid()), new PlayerID(Guid.NewGuid()),
                "FDG.StageResolution.Requests.YesNoRequest", "System.Boolean", "Use Martial Prowess?",
                "{\"Question\":\"Reactivate?\",\"$type\":\"x\"}", ReplyJson: "true", ErrorMessage: null);
            var failed = answered with { TaskID = new TaskID(Guid.NewGuid()), ReplyJson = null, ErrorMessage = "Player disconnected." };

            Assert.That(receiver.DeserializeMessage(sender.SerializeMessage(answered)), Is.EqualTo(answered));
            Assert.That(receiver.DeserializeMessage(sender.SerializeMessage(failed)), Is.EqualTo(failed));
        }
    }
}
