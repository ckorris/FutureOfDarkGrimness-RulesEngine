using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class ChargeReachValidationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public void WithinRush_NoMeleeNearby_Accepted()
        {
            //Moved 10" (< 12" Rush). Reach rule does not apply.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 0) });

            //No enemies anywhere.
            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxRushDistance: 12f,
                maxDistanceInches: 18f,
                enemyFootprints: new List<EnemyModelFootprint>(),
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ChargeRangeRequiresMeleeReach), Is.False);
        }

        [Test]
        public void BeyondRush_EndsInMelee_Accepted()
        {
            //Moved 15" (> 12" Rush, ≤ 18" Charge). Ends with a BASE-to-base gap of 1.9" (within
            //MELEE_RANGE_INCHES_HORIZONTAL = 2"): centres 3.4" apart, radii 0.75" each. #312: the old
            //centre-to-centre check would have rejected exactly this legal charge.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(15, 0) });

            List<EnemyModelFootprint> enemies = new List<EnemyModelFootprint> { new EnemyModelFootprint(new Position(18.4f, 0), 0.75f, 0) };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxRushDistance: 12f,
                maxDistanceInches: 18f,
                enemyFootprints: enemies,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True, "Expected accept; got errors: "
                + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString())));
        }

        [Test]
        public void BeyondRush_NoMeleeReach_Rejected()
        {
            //Moved 15" (> 12" Rush). Nearest enemy is 3.5" away base-to-base (centres 5", radii 0.75")
            //— outside melee range.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(15, 0) });

            List<EnemyModelFootprint> enemies = new List<EnemyModelFootprint> { new EnemyModelFootprint(new Position(20, 0), 0.75f, 0) };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxRushDistance: 12f,
                maxDistanceInches: 18f,
                enemyFootprints: enemies,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ChargeRangeRequiresMeleeReach), Is.True);
        }

        [Test]
        public void BeyondRush_DifferentModelEndsInMelee_Accepted()
        {
            //One model overshoots Rush; a different (slower) model in the same unit ends in melee.
            //Rule is satisfied — "at least one model" need not be the one that overshot.
            DataBinding<ModelData> overshooter = MakeModel(new Position(0, 0));
            DataBinding<ModelData> closer      = MakeModel(new Position(10, 0));

            //Overshooter goes 13" (beyond Rush) but stops clear of the enemy — NOT in melee and not stacked
            //on it (ending on top of an enemy base is now its own MovingThroughEnemyUnit error).
            ModelMoveEntry overshooterMove = new ModelMoveEntry(overshooter,
                new List<Position> { new Position(13, 0) });

            //A different (slower) model ends within melee range, satisfying the reach rule for the unit.
            ModelMoveEntry closerMove = new ModelMoveEntry(closer,
                new List<Position> { new Position(14, 0) });

            List<EnemyModelFootprint> enemies = new List<EnemyModelFootprint> { new EnemyModelFootprint(new Position(16, 0), 0.75f, 0) };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { overshooterMove, closerMove },
                maxRushDistance: 12f,
                maxDistanceInches: 18f,
                enemyFootprints: enemies,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True, "Expected accept; got errors: "
                + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString())));
        }

        [Test]
        public void BeyondRush_BigRectBase_BaseContact_Accepted()
        {
            //#312 regression: the Micro-Titan case from ChargeOfferedButWontAllow.fdgsave. A model on a
            //large rectangular base (2.76" x 4.13") charges beyond Rush and ends its nose 0.1" from a small
            //round base. Centres are ~2.66" apart — the old centre-to-centre check rejected this even in
            //literal base contact, making big-base charges mathematically impossible.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0),
                new RectangleBase(2.7559056f, 4.133858f));
            //Default facing (0,1): the 4.13" height axis runs along Z. End at (15, 0); enemy is off the
            //nose at z = halfHeight + enemyRadius + 0.1 = 2.659.
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(15, 0) });

            List<EnemyModelFootprint> enemies = new List<EnemyModelFootprint>
                { new EnemyModelFootprint(new Position(15f, 2.659f), 0.492126f, 0) };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxRushDistance: 12f,
                maxDistanceInches: 18f,
                enemyFootprints: enemies,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True, "Expected accept; got errors: "
                + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString())));
        }

        [Test]
        public void BeyondRush_ReachOnlyAtEndFacing_Accepted()
        {
            //#312: the reach check must measure the base at its END facing (the orientation the executor
            //will leave it at), not its pre-move resting facing. A 1" x 4" base facing +Z (long axis along
            //Z) turns to face +X during the move: at the end facing its long axis reaches the enemy on the
            //X axis (gap 0.7"), while at the resting facing the gap is 2.2" — out of range.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0), new RectangleBase(1f, 4f));
            ModelMoveEntry move = new ModelMoveEntry(model,
                new List<Position> { new Position(0.5f, 0) },
                new List<Float2> { new Float2(1f, 0f) });

            List<EnemyModelFootprint> enemies = new List<EnemyModelFootprint>
                { new EnemyModelFootprint(new Position(3.6f, 0), 0.4f, 0) };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxRushDistance: 0.3f, //The 0.5" move exceeds Rush, so the reach rule applies.
                maxDistanceInches: 18f,
                enemyFootprints: enemies,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True, "Expected accept; got errors: "
                + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString())));
        }

        [Test]
        public void RushEqualsCharge_BeyondRush_ImpossibleByCap()
        {
            //Sanity: when Rush == Charge, no path can legally exceed Rush (the hard cap is the same).
            //Validator should accept anything within that cap regardless of melee reach.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(11, 0) });

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxRushDistance: 12f,
                maxDistanceInches: 12f,
                enemyFootprints: new List<EnemyModelFootprint>(),
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True);
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition, IBaseShape? baseShape = null)
        {
            ModelData modelData = new ModelData(
                baseShape ?? new CircleBase(0.75f),
                weapons: new List<Weapon>(),
                initialPosition: initialPosition,
                gameDataStore: _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }
    }
}
