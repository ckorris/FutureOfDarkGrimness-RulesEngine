using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.Players;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace FDG.Tests
{
    // #082 — AI & player-controller lifecycle nits.
    [TestFixture]
    public class AiControllerLifecycleTests
    {
        // The AI must answer each yes/no with the default the request declares, not a blanket "always yes",
        // so a future question whose correct AI answer is "no" is honored instead of silently accepted.
        [TestCase(true)]
        [TestCase(false)]
        public async Task AiYesNoResolver_HonorsAiPrefersYes(bool aiPrefersYes)
        {
            var resolver = new AiYesNoResolver();
            var request = new YesNoRequest(new PlayerID(Guid.NewGuid()), "Do the thing?", aiPrefersYes: aiPrefersYes);

            bool answer = await resolver.Resolve(request);

            Assert.That(answer, Is.EqualTo(aiPrefersYes),
                "the AI resolver returns the per-question AI default carried on the request.");
        }

        // The AI default rides the request, so it must survive the wire to the host's AI resolver.
        [Test]
        public void YesNoRequest_AiPrefersYes_RoundTripsJson()
        {
            var request = new YesNoRequest(new PlayerID(Guid.NewGuid()), "Decline this?", aiPrefersYes: false);

            string json = JsonConvert.SerializeObject(request);
            var deserialized = JsonConvert.DeserializeObject<YesNoRequest>(json);

            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.AiPrefersYes, Is.False);
            Assert.That(deserialized.QuestionText, Is.EqualTo("Decline this?"));
        }

        // Older serialized requests (or any constructed without the flag) default to the opt-in answer.
        [Test]
        public void YesNoRequest_MissingAiPreference_DefaultsToYes()
        {
            string json = "{\"TargetPlayerID\":{\"ID\":\"" + Guid.NewGuid() + "\"}," +
                          "\"TaskID\":{\"ID\":\"" + Guid.NewGuid() + "\"},\"QuestionText\":\"Legacy?\"}";

            var deserialized = JsonConvert.DeserializeObject<YesNoRequest>(json);

            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.AiPrefersYes, Is.True, "a request with no declared AI preference opts in.");
        }

        // A duplicate PostLaunchPlayerReadyMessage must not re-fire OnReadyStateChanged: the bus can deliver
        // the same ready message more than once.
        [Test]
        public void NetworkPlayerController_DuplicateReadyMessage_FiresStateChangeOnce()
        {
            var bus = new RequestSystemTests.MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());
            var controller = new NetworkPlayerController("Net Player", playerID, ConnectionID.Host, bus,
                GameDataStore.GameDataStoreBuilder.GetDefault());

            int fireCount = 0;
            controller.OnReadyStateChanged += _ => fireCount++;

            bus.SimulateMessageReceived(new PostLaunchPlayerReadyMessage(playerID));
            bus.SimulateMessageReceived(new PostLaunchPlayerReadyMessage(playerID));

            Assert.That(controller.IsReady, Is.True);
            Assert.That(fireCount, Is.EqualTo(1), "the second ready message is idempotent and does not re-fire.");
        }

        // A ready message for a different player must not flip this controller ready.
        [Test]
        public void NetworkPlayerController_OtherPlayerReadyMessage_Ignored()
        {
            var bus = new RequestSystemTests.MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());
            var controller = new NetworkPlayerController("Net Player", playerID, ConnectionID.Host, bus,
                GameDataStore.GameDataStoreBuilder.GetDefault());

            int fireCount = 0;
            controller.OnReadyStateChanged += _ => fireCount++;

            bus.SimulateMessageReceived(new PostLaunchPlayerReadyMessage(new PlayerID(Guid.NewGuid())));

            Assert.That(controller.IsReady, Is.False);
            Assert.That(fireCount, Is.EqualTo(0));
        }
    }
}
