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
            //Moved 15" (> 12" Rush, ≤ 18" Charge). Ends 1.5" from enemy (within MELEE_RANGE_INCHES_HORIZONTAL = 2").
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(15, 0) });

            List<EnemyModelFootprint> enemies = new List<EnemyModelFootprint> { new EnemyModelFootprint(new Position(16.5f, 0), 0.75f, 0) };

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
            //Moved 15" (> 12" Rush). Nearest enemy is 5" away — outside melee range.
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

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            ModelData modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: initialPosition,
                gameDataStore: _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }
    }
}
