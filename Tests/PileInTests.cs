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

        [Test]
        public void DefenderPilingTowardCharger_DoesNotPlowThroughAThirdPartyEnemy()
        {
            // #159: the defender is charged by a charger far to its right, so it wants to pile the full 3"
            // toward it — but a DIFFERENT (large) enemy base sits between them. Pile-in used to only avoid the
            // charging unit's models, so the defender plowed straight into the third-party base (deep overlap),
            // leaving a model stacked inside an enemy. It must stop at contact with that base instead.
            var defender = MakeModel(new Position(0f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(10f, 0f), radius: 0.5f);
            // Third-party enemy: a big base centred at (3,0), radius 1.0 (spans x 2..4), directly in the lane.
            var thirdParty = new EnemyModelFootprint(new Position(3f, 0f), baseRadiusInches: 1.0f, unitKey: 0,
                uncontactable: false, baseShape: new CircleBase(1.0f), facing: new Float2(0f, 1f));

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { defender },
                terrain: null,
                otherEnemyModels: new[] { thirdParty });

            Position end = moves.Count > 0 ? moves[0].NewPosition : defender.GetValue().Position;

            // The defender advanced toward the charger...
            Assert.That(end.x, Is.GreaterThan(0.5f), "defender should still pile in toward the charger.");
            // ...but did NOT end overlapping the third-party base beyond the contact tolerance.
            float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                end, new Position(3f, 0f), new CircleBase(0.5f), new CircleBase(1.0f));
            Assert.That(gap, Is.GreaterThanOrEqualTo(-0.11f),
                $"defender must stop at contact with the third-party enemy, not overlap it (gap {gap:F3}).");
        }

        // --- #330: contact-slot pile-in — the unit envelops instead of keeping formation -------------

        [Test]
        public void SecondRankDefender_SlidesAroundFriend_IntoContact()
        {
            // D1 at (1,0) already in base contact with the charger at (0,0). D2 at (2,0) sits directly
            // behind D1. The pre-#330 ray-march stopped D2 dead against D1's back (formation-keeping);
            // slot assignment must route it to an open flank slot in true base contact with the charger.
            var d1 = MakeModel(new Position(1f, 0f), radius: 0.5f);
            var d2 = MakeModel(new Position(2f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { d1, d2 },
                terrain: null);

            Assert.That(moves, Has.Count.EqualTo(1), "only D2 moves; D1 is already in contact.");
            Assert.That(moves[0].Model, Is.SameAs(d2));
            Position end = moves[0].NewPosition;

            float b2bCharger = DistanceUtilities.GetBaseToBaseDistanceInches_2D(end, charger.GetValue().Position, 0.5f, 0.5f);
            Assert.That(b2bCharger, Is.LessThanOrEqualTo(0.011f), "D2 must reach base contact with the charger.");
            float b2bFriend = DistanceUtilities.GetBaseToBaseDistanceInches_2D(end, d1.GetValue().Position, 0.5f, 0.5f);
            Assert.That(b2bFriend, Is.GreaterThanOrEqualTo(-0.005f), "D2 must not overlap D1 at its final spot.");
        }

        [Test]
        public void ThreeDefenders_AllReachContact_SurroundingTheCharger()
        {
            // Three defenders east of a lone charger. Ray-marching got exactly one into contact (the
            // others piled up behind); slot assignment must wrap all three around the charger's base.
            var dA = MakeModel(new Position(2f, 0f), radius: 0.5f);
            var dB = MakeModel(new Position(2f, 1f), radius: 0.5f);
            var dC = MakeModel(new Position(2f, -1f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);
            var defenders = new[] { dA, dB, dC };

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: defenders,
                terrain: null);

            Assert.That(moves, Has.Count.EqualTo(3), "all three defenders can reach a slot within 3\".");

            var finals = new Dictionary<DataBinding<ModelData>, Position>();
            foreach (var d in defenders) finals[d] = d.GetValue().Position;
            foreach (var m in moves) finals[m.Model] = m.NewPosition;

            foreach (var d in defenders)
            {
                float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(finals[d], charger.GetValue().Position, 0.5f, 0.5f);
                Assert.That(b2b, Is.LessThanOrEqualTo(0.011f), "every defender ends in base contact with the charger.");
            }
            for (int i = 0; i < defenders.Length; i++)
            {
                for (int j = i + 1; j < defenders.Length; j++)
                {
                    float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(finals[defenders[i]], finals[defenders[j]], 0.5f, 0.5f);
                    Assert.That(gap, Is.GreaterThanOrEqualTo(-0.005f), "defenders must not overlap each other.");
                }
            }
        }

        [Test]
        public void TerrainPartiallyBlocksApproach_DefenderSlidesToOpenSlot()
        {
            // A thin impassable wall dips just into the direct lane from the defender at (3,0) to the
            // charger at (0,0): it spans x 1.45..1.55, z 0.35..3.0, so the straight approach (swept band
            // z -0.5..0.5 at the wall) is blocked, as is every northern slot — but the southern flank is
            // open. The pre-#330 behavior skipped the move entirely (terrain on the ray = stay put);
            // slot assignment must take a southern contact slot instead.
            var defender = MakeModel(new Position(3f, 0f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var wall = new RectangularZone(1.45f, 1.55f, 0.35f, 3f);
            var terrain = new List<ITerrain> { new TerrainData(ETerrainType.Impassible, wall) };

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { defender },
                terrain: terrain);

            Assert.That(moves, Has.Count.EqualTo(1), "an open flank slot exists, so the defender must move.");
            Position end = moves[0].NewPosition;

            float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(end, charger.GetValue().Position, 0.5f, 0.5f);
            Assert.That(b2b, Is.LessThanOrEqualTo(0.011f), "the defender reaches base contact despite the wall.");
            Assert.That(end.z, Is.LessThan(-0.1f), "it must have gone around the SOUTH side (the open flank).");
        }

        [Test]
        public void TerrainBlocksDirectLaneOnly_SecondDefenderStillWrapsClear()
        {
            // Two defenders share the blocked eastern lane (same wall as above, moved to guard only the
            // corridor). Both must find open southern slots without overlapping each other or the wall's
            // side of the ring.
            var d1 = MakeModel(new Position(3f, -0.4f), radius: 0.5f);
            var d2 = MakeModel(new Position(4f, -0.4f), radius: 0.5f);
            var charger = MakeModel(new Position(0f, 0f), radius: 0.5f);

            var wall = new RectangularZone(1.45f, 1.55f, 0.35f, 3f);
            var terrain = new List<ITerrain> { new TerrainData(ETerrainType.Impassible, wall) };

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { d1, d2 },
                terrain: terrain);

            // D1 can reach a southern slot (about 2.1-2.3" away). D2 starts ~3" behind; whether it reaches
            // contact or falls back, it must never overlap D1 or cross the wall.
            Assert.That(moves, Has.Count.GreaterThanOrEqualTo(1), "at least the near defender piles in.");

            Position d1Final = d1.GetValue().Position;
            Position d2Final = d2.GetValue().Position;
            foreach (var m in moves)
            {
                if (m.Model == d1) d1Final = m.NewPosition;
                if (m.Model == d2) d2Final = m.NewPosition;
            }

            float d1B2B = DistanceUtilities.GetBaseToBaseDistanceInches_2D(d1Final, charger.GetValue().Position, 0.5f, 0.5f);
            Assert.That(d1B2B, Is.LessThanOrEqualTo(0.011f), "the near defender reaches base contact.");

            float pairGap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(d1Final, d2Final, 0.5f, 0.5f);
            Assert.That(pairGap, Is.GreaterThanOrEqualTo(-0.005f), "defenders must not overlap.");
        }

        [Test]
        public void RectangularCharger_BothDefendersReachTrueEdgeContact()
        {
            // A 3"x1" rectangular charger. Two round defenders approach its eastern short edge from
            // offset angles; the slot search must settle both at TRUE oriented-edge contact (via
            // SurfaceGap2D), not bounding-circle contact, and without overlapping each other.
            var charger = MakeModel(new RectangleBase(3f, 1f), new Position(0f, 0f));
            var d1 = MakeModel(new Position(2.6f, 0.8f), radius: 0.5f);
            var d2 = MakeModel(new Position(2.6f, -0.8f), radius: 0.5f);

            var moves = PileInUtilities.ComputePileInMoves(
                chargingModels: new[] { charger },
                defendingModels: new[] { d1, d2 },
                terrain: null);

            Assert.That(moves, Has.Count.EqualTo(2), "both defenders have reachable slots on the eastern edge.");

            Position d1Final = moves.First(m => m.Model == d1).NewPosition;
            Position d2Final = moves.First(m => m.Model == d2).NewPosition;

            float d1Gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                d1Final, charger.GetValue().Position, d1.GetValue().BaseShape, charger.GetValue().BaseShape);
            float d2Gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                d2Final, charger.GetValue().Position, d2.GetValue().BaseShape, charger.GetValue().BaseShape);
            Assert.That(d1Gap, Is.LessThanOrEqualTo(0.011f), "D1 ends in true edge contact with the rectangle.");
            Assert.That(d1Gap, Is.GreaterThanOrEqualTo(-0.005f), "D1 must not overlap the rectangle.");
            Assert.That(d2Gap, Is.LessThanOrEqualTo(0.011f), "D2 ends in true edge contact with the rectangle.");
            Assert.That(d2Gap, Is.GreaterThanOrEqualTo(-0.005f), "D2 must not overlap the rectangle.");

            float pairGap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(d1Final, d2Final, 0.5f, 0.5f);
            Assert.That(pairGap, Is.GreaterThanOrEqualTo(-0.005f), "defenders must not overlap each other.");
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
