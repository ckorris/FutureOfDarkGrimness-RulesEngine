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
                    new ModelMove(new ModelID(Guid.NewGuid()),
                        new List<Position> { new Position(1f, 2f), new Position(3f, 4f) }),
                },
                PresentationDurations.UnitMove);

            Assert.That(beat.NominalDuration, Is.EqualTo(PresentationDurations.UnitMove));
            Assert.That(beat.Text, Is.EqualTo("Warriors moves."));
        }

        [Test]
        public void UnitMovedBeat_SurvivesWireRoundTrip_PreservingTypeMovesAndDuration()
        {
            var unitId = new UnitID(Guid.NewGuid());
            var modelId = new ModelID(Guid.NewGuid());
            // Multi-node polyline: start -> corner -> destination.
            var original = new UnitMovedBeat(unitId, "Warriors",
                new List<ModelMove>
                {
                    new ModelMove(modelId, new List<Position>
                    {
                        new Position(1f, 2f), new Position(1f, 5f), new Position(4f, 5f),
                    }),
                },
                TimeSpan.FromMilliseconds(900));

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<UnitMovedBeat>(), "concrete beat type must survive TypeNameHandling.Auto");
            var moved = (UnitMovedBeat)result;
            Assert.That(moved.Unit, Is.EqualTo(unitId));
            Assert.That(moved.UnitName, Is.EqualTo("Warriors"));
            Assert.That(moved.NominalDuration, Is.EqualTo(TimeSpan.FromMilliseconds(900)),
                "carried duration must round-trip so distance-based pacing survives the wire");
            Assert.That(moved.Moves, Has.Count.EqualTo(1));
            ModelMove m = moved.Moves[0];
            Assert.That(m.Model, Is.EqualTo(modelId));
            Assert.That(m.Waypoints, Has.Count.EqualTo(3), "the full corner-rounding polyline must survive");
            Assert.That(m.Waypoints[0].x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(m.Waypoints[0].z, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(m.Waypoints[1].x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(m.Waypoints[1].z, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(m.Waypoints[2].x, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(m.Waypoints[2].z, Is.EqualTo(5f).Within(0.0001f));
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

        [Test]
        public void DiceRolledBeat_From_CapturesHistogramAndComputesSuccesses()
        {
            // d6: one 2, two 4s, one 6  →  4 dice, 3 of them at-or-above 4.
            var roll = new DiceResults(new float[] { 0f, 1f, 0f, 2f, 0f, 1f }, 1);

            var beat = DiceRolledBeat.From(roll, successThreshold: 4, ERandomnessType.Realistic, "To Hit");

            Assert.That(beat.SideMin, Is.EqualTo(1));
            Assert.That(beat.SideMax, Is.EqualTo(6));
            Assert.That(beat.Total, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(beat.Successes, Is.EqualTo(3f).Within(0.0001f), "faces 4,4,6 are >= 4");
            Assert.That(beat.Text, Is.EqualTo("To Hit (4+): 3 / 4"));
        }

        [Test]
        public void DiceRolledBeat_Realistic_SurvivesWireRoundTrip()
        {
            var original = new DiceRolledBeat(new List<float> { 0f, 1f, 0f, 2f, 0f, 1f },
                sideMin: 1, successThreshold: 4, ERandomnessType.Realistic, "To Hit");

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<DiceRolledBeat>());
            var dice = (DiceRolledBeat)result;
            Assert.That(dice.FaceCounts, Is.EqualTo(new[] { 0f, 1f, 0f, 2f, 0f, 1f }));
            Assert.That(dice.SideMin, Is.EqualTo(1));
            Assert.That(dice.SuccessThreshold, Is.EqualTo(4));
            Assert.That(dice.Mode, Is.EqualTo(ERandomnessType.Realistic));
            Assert.That(dice.Label, Is.EqualTo("To Hit"));
        }

        [Test]
        public void AttackBeat_Ranged_SurvivesWireRoundTrip_PreservingPositionsAndKind()
        {
            // Two firing weapons (From), two volleys.
            var original = new AttackBeat(isMelee: false,
                from: new List<Position> { new Position(1f, 2f), new Position(3f, 2f) },
                to: new List<Position> { new Position(10f, 20f) },
                volleyCount: 2, armorPenetration: 2);

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<AttackBeat>());
            var atk = (AttackBeat)result;
            Assert.That(atk.IsMelee, Is.False);
            Assert.That(atk.VolleyCount, Is.EqualTo(2));
            Assert.That(atk.ArmorPenetration, Is.EqualTo(2));
            Assert.That(atk.NominalDuration, Is.EqualTo(PresentationDurations.ForVolleys(2)),
                "duration scales with the volley count (all From weapons fire together per volley)");
            Assert.That(atk.From, Has.Count.EqualTo(2));
            Assert.That(atk.To, Has.Count.EqualTo(1));
            Assert.That(atk.From[1].x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(atk.To[0].z, Is.EqualTo(20f).Within(0.0001f));
        }

        [Test]
        public void AttackBeat_Melee_DurationScalesWithVolleyCount()
        {
            var melee = new AttackBeat(isMelee: true,
                from: new List<Position> { new Position(1f, 1f) },
                to: new List<Position> { new Position(2f, 1f) },
                volleyCount: 4, armorPenetration: 0);

            var result = (AttackBeat)RoundTrip(melee);
            Assert.That(result.IsMelee, Is.True);
            Assert.That(result.VolleyCount, Is.EqualTo(4));
            Assert.That(result.NominalDuration, Is.EqualTo(PresentationDurations.ForVolleys(4)));
        }

        [Test]
        public void ModelWoundedBeat_SurvivesWireRoundTrip()
        {
            var modelId = new ModelID(Guid.NewGuid());
            var original = new ModelWoundedBeat(modelId, new Position(4f, 7f));

            var result = (ModelWoundedBeat)RoundTrip(original);
            Assert.That(result.Model, Is.EqualTo(modelId));
            Assert.That(result.Position.x, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(result.Position.z, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(result.NominalDuration, Is.EqualTo(PresentationDurations.ModelWounded));
        }

        [Test]
        public void SaveBeat_SurvivesWireRoundTrip_PreservingCountAndPositions()
        {
            var original = new SaveBeat(
                new List<Position> { new Position(1f, 1f), new Position(2f, 2f) },
                savedCount: 3);

            var result = (SaveBeat)RoundTrip(original);
            Assert.That(result.SavedCount, Is.EqualTo(3));
            Assert.That(result.DefenderPositions, Has.Count.EqualTo(2));
            Assert.That(result.DefenderPositions[1].x, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void BannerBeat_SurvivesWireRoundTrip_PreservingTextAndColor()
        {
            var original = new BannerBeat("Round 3", new TextColor(120, 200, 255, 255));

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<BannerBeat>());
            var banner = (BannerBeat)result;
            Assert.That(banner.BannerText, Is.EqualTo("Round 3"));
            Assert.That(banner.Text, Is.EqualTo("Round 3"), "Text projection mirrors the banner text");
            Assert.That(banner.Color, Is.EqualTo(new TextColor(120, 200, 255, 255)));
        }

        [Test]
        public void DiceRolledBeat_Probabilistic_RoundTrip_PreservesFractionsAndMode()
        {
            // 5 dice spread evenly: 5/6 per face. Successes for 4+ = three faces * 0.8333…
            float per = 5f / 6f;
            var original = new DiceRolledBeat(new List<float> { per, per, per, per, per, per },
                sideMin: 1, successThreshold: 4, ERandomnessType.Probabilistic, "To Save");

            PresentationBeat result = RoundTrip(original);

            var dice = (DiceRolledBeat)result;
            Assert.That(dice.Mode, Is.EqualTo(ERandomnessType.Probabilistic),
                "the mode must ride on the beat — the front-end renders fractional rolls differently");
            Assert.That(dice.Total, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(dice.Successes, Is.EqualTo(3f * per).Within(0.0001f));
            for (int i = 0; i < dice.FaceCounts.Count; i++)
                Assert.That(dice.FaceCounts[i], Is.EqualTo(per).Within(0.0001f), "fractional counts must survive the wire");
        }
    }
}
