using System;
using System.Collections.Generic;
using FDG.Data;
using FDG.Network.Messages;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Presentation.Messages;
using NUnit.Framework;

namespace FDG.Tests
{
    // The slice-1 beats are polymorphic and travel host->client inside a PresentBeatMessage, so
    // they must survive the real bus serializer (TypeNameHandling.Auto). A broken round-trip would
    // silently mean networked clients render nothing. Driving the full emission flow through the
    // state machine is impractical in a unit test (StageBinding.Activate needs a bound transition +
    // real parent), so emission-in-context is left to the #7 integration pass; here we lock down the
    // beat payloads and their wire round-trip.
    [TestFixture]
    public class PresentationBeatSerializationTests
    {
        private MessageSerializer _serializer = null!;

        [SetUp]
        public void SetUp()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _serializer = new MessageSerializer(store);
            _serializer.RegisterMessageType<PresentBeatMessage>();
        }

        private PresentationBeat RoundTrip(PresentationBeat beat)
        {
            var sent = new PresentBeatMessage(beat);
            ArraySegment<byte> bytes = _serializer.SerializeMessage(sent);
            var received = _serializer.DeserializeMessage(bytes) as PresentBeatMessage;
            Assert.That(received, Is.Not.Null, "PresentBeatMessage failed to deserialize");
            return received!.Beat;
        }

        [Test]
        public void UnitMovedBeat_CarriesPayloadAndProjection()
        {
            var beat = new UnitMovedBeat(
                new UnitID(Guid.NewGuid()),
                "Warriors",
                new List<ModelMove>
                {
                    new ModelMove(new ModelID(Guid.NewGuid()), new Position(1f, 2f), new Position(3f, 4f)),
                });

            Assert.That(beat.NominalDuration, Is.EqualTo(PresentationDurations.UnitMove));
            Assert.That(beat.Text, Is.EqualTo("Warriors moves."));
        }

        [Test]
        public void UnitMovedBeat_SurvivesWireRoundTrip_PreservingConcreteTypeAndMoves()
        {
            var unitId = new UnitID(Guid.NewGuid());
            var modelId = new ModelID(Guid.NewGuid());
            var original = new UnitMovedBeat(unitId, "Warriors",
                new List<ModelMove> { new ModelMove(modelId, new Position(1f, 2f), new Position(3f, 4f)) });

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<UnitMovedBeat>(), "concrete beat type must survive TypeNameHandling.Auto");
            var moved = (UnitMovedBeat)result;
            Assert.That(moved.Unit, Is.EqualTo(unitId));
            Assert.That(moved.UnitName, Is.EqualTo("Warriors"));
            Assert.That(moved.Moves, Has.Count.EqualTo(1));
            ModelMove m = moved.Moves[0];
            Assert.That(m.Model, Is.EqualTo(modelId));
            Assert.That(m.From.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(m.From.z, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(m.To.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(m.To.z, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void ModelDiedBeat_CarriesPayloadAndProjection()
        {
            var beat = new ModelDiedBeat(new ModelID(Guid.NewGuid()), new UnitID(Guid.NewGuid()),
                "Heavy Gunners", new Position(5f, 6f));

            Assert.That(beat.NominalDuration, Is.EqualTo(PresentationDurations.ModelDeath));
            Assert.That(beat.Text, Is.EqualTo("Heavy Gunners: a model is destroyed."));
        }

        [Test]
        public void ModelDiedBeat_SurvivesWireRoundTrip_PreservingConcreteTypeAndPosition()
        {
            var modelId = new ModelID(Guid.NewGuid());
            var unitId = new UnitID(Guid.NewGuid());
            var original = new ModelDiedBeat(modelId, unitId, "Heavy Gunners", new Position(5f, 6f));

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<ModelDiedBeat>());
            var died = (ModelDiedBeat)result;
            Assert.That(died.Model, Is.EqualTo(modelId));
            Assert.That(died.Unit, Is.EqualTo(unitId));
            Assert.That(died.UnitName, Is.EqualTo("Heavy Gunners"));
            Assert.That(died.Position.x, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(died.Position.z, Is.EqualTo(6f).Within(0.0001f));
        }
    }
}
