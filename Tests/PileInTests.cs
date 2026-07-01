using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class PileInTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public void DefenderAlreadyInBaseContact_NoMove()
        {
            // Defender at (1, 0), Charger at (0, 0). Bases touching (b2b = 0).
            var defender = MakeModel(new Position(1f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { defender },
                terrain: null);

            Assert.That(moves, Is.Empty);
        }

        [Test]
        public void DefenderWithinPileInRange_MovesToBaseContact()
        {
            // Defender at (2, 0), Charger at (0, 0). b2b = 1.0", within 3" pile-in cap.
            var defender = MakeModel(new Position(2f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { defender },
                terrain: null);

            Assert.That(moves, Has.Count.EqualTo(1));
            var newPos = moves[0].NewPosition;
            float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(newPos, charger.GetValue().Position, 0.5f, 0.5f);
            Assert.That(b2b, Is.LessThanOrEqualTo(0.01f), "Defender should end at base contact.");
        }

        [Test]
        public void RectangularDefender_PilesInToTrueEdgeContact_NotBoundingCircle()
        {
            // A 4"×1" defender at (5,0) piling toward a charger circle (r=0.5) at (0,0). Its 2" half-width leaves
            // a true 2.5" gap (< the 3" cap), so it advances 2.5" to real edge contact. An inscribed bounding
            // circle (r=0.5) would read a 4" gap, cap at 3", and overshoot into overlap (#150).
            var defender = MakeModel(new RectangleBase(4f, 1f), new Position(5f, 0f));
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { defender },
                terrain: null);

            Assert.That(moves, Has.Count.EqualTo(1));
            Position newPos = moves[0].NewPosition;
            float trueB2B = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                newPos, charger.GetValue().Position, defender.GetValue().BaseShape, charger.GetValue().BaseShape);
            Assert.That(trueB2B, Is.EqualTo(0f).Within(0.05f), "the rectangular base ends at true edge contact, not overlapping.");
            Assert.That(5f - newPos.x, Is.EqualTo(2.5f).Within(0.05f),
                "it advances only to true contact (~2.5\"), not the full 3\" a bounding circle would allow.");
        }

        [Test]
        public void DefenderBeyondPileInRange_MovesExactlyThreeInches()
        {
            // Defender at (5, 0), Charger at (0, 0). b2b = 4". Cap at 3" → ends 1" b2b.
            var defender = MakeModel(new Position(5f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { defender },
                terrain: null);

            Assert.That(moves, Has.Count.EqualTo(1));
            var newPos = moves[0].NewPosition;
            float distMoved = MathF.Sqrt(MathF.Pow(newPos.x - 5f, 2) + MathF.Pow(newPos.z, 2));
            Assert.That(distMoved, Is.EqualTo(3f).Within(0.01f));
        }

        [Test]
        public void MixedUnit_OnlyNonBaseContactModelsMove()
        {
            // D1 at (1, 0) is in BTB with charger at (0, 0). D2 at (3, 0) is 1.5" b2b away.
            var d1 = MakeModel(new Position(1f, 0f), radius: 0.5f);
            var d2 = MakeModel(new Position(3f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { d1, d2 },
                terrain: null);

            // Only d2 should appear in the moves list.
            Assert.That(moves, Has.Count.EqualTo(1));
            Assert.That(moves[0].Model, Is.SameAs(d2));
        }

        [Test]
        public void ImpassableTerrainOnPath_DefenderDoesNotMove()
        {
            // Defender at (5, 0) would pile in toward charger at (0, 0); impassable terrain at x=2..3 blocks.
            var defender = MakeModel(new Position(5f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new RectangularZone(2f, 3f, -2f, 2f))
            };

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { defender },
                terrain: terrain);

            Assert.That(moves, Is.Empty);
        }

        [Test]
        public void PathBlockedByAnotherDefender_StepShortenedNoOverlap()
        {
            // D1 at (4, 0), D2 at (5, 0), Charger at (0, 0). Both want to pile in left.
            // D1 (closer to charger) processed first; should move ~3" to (1, 0).
            // D2's path passes through D1's new spot — step capped so D2's base just meets D1's base.
            var d1 = MakeModel(new Position(4f, 0f), radius: 0.5f);
            var d2 = MakeModel(new Position(5f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { d1, d2 },
                terrain: null);

            Assert.That(moves, Has.Count.EqualTo(2));
            var d1Final = moves.First(m => m.Model == d1).NewPosition;
            var d2Final = moves.First(m => m.Model == d2).NewPosition;
            float d1d2_b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(d1Final, d2Final, 0.5f, 0.5f);
            Assert.That(d1d2_b2b, Is.GreaterThanOrEqualTo(-0.005f),
                "After pile-in, defenders must not overlap each other.");
        }

        [Test]
        public void PileInResultLeavesUnitCoherent()
        {
            // Two defenders close together, two chargers pulling them in opposite directions far enough that
            // an unchecked pile-in would put them >1" b2b apart. Algorithm must end coherent — either by
            // reverting moves or by accepting them only when consistent.
            var d1 = MakeModel(new Position(0f, 0f), radius: 0.5f);
            var d2 = MakeModel(new Position(0f, 1f), radius: 0.5f); // b2b D1-D2 = 0.0" initially (touching).

            var chargerLeft = MakeModel(new Position(-10f, 0f), radius: 0.5f);   // pulls D1 left
            var chargerRight = MakeModel(new Position(10f, 1f), radius: 0.5f);   // pulls D2 right

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { chargerLeft, chargerRight },
                defendingModels: new[] { d1, d2 },
                terrain: null);

            // Apply the moves (or treat as no-op for unmoved defenders) and verify coherency.
            Position d1Final = moves.FirstOrDefault(m => m.Model == d1).Model != null
                ? moves.First(m => m.Model == d1).NewPosition : d1.GetValue().Position;
            Position d2Final = moves.FirstOrDefault(m => m.Model == d2).Model != null
                ? moves.First(m => m.Model == d2).NewPosition : d2.GetValue().Position;

            float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_3D(d1Final, d2Final, 0.5f, 0.5f);
            Assert.That(b2b, Is.LessThanOrEqualTo(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES + 0.01f),
                "Defenders must remain within 1\" b2b after pile-in.");
        }

        private DataBinding<ModelData> MakeModel(IBaseShape shape, Position initialPosition)
        {
            var modelData = new ModelData(shape, new List<Weapon>(), initialPosition, _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition, float radius)
        {
            var modelData = new ModelData(
                baseRadiusInches: radius,
                weapons: new List<Weapon>(),
                initialPosition: initialPosition,
                gameDataStore: _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }
    }
}
