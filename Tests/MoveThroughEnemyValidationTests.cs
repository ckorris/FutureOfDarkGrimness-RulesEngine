using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #011 — ValidateMovingThroughEnemyUnits: a move may not pass through or stack on an enemy base, and a
    // non-charging model must end at least ENEMY_STANDOFF_DISTANCE_INCHES (base-to-base) from enemies.
    // Ending in/near base contact counts as charging and waives the standoff for that whole enemy unit.
    // All models here have radius 0.75", so base contact is at 1.5" centre-to-centre.
    [TestFixture]
    public class MoveThroughEnemyValidationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public void PathPassesThroughEnemyBase_Rejected()
        {
            //Straight line from (0,0) to (10,0) runs over an enemy sitting at (5,0).
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughEnemyUnit), Is.True);
        }

        [Test]
        public void PathSkirtsEnemyBase_Accepted()
        {
            //Travels along z=5, well clear of the enemy down at (5,0).
            DataBinding<ModelData> model = MakeModel(new Position(0, 5));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 5) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void NonChargeEndsInsideStandoff_Rejected()
        {
            //Ends at (2.6,0): 2.4" centre-to-centre from the enemy (0.9" base-to-base) — inside the 1"
            //standoff but not close enough to count as a charge.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(2.6f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedTooCloseToEnemy), Is.True);
        }

        [Test]
        public void ChargeEndsInBaseContact_Accepted()
        {
            //Ends at (3.5,0): exactly base-to-base with the enemy at (5,0). Legal charge into contact.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void ChargeOvershootsIntoOverlap_Rejected()
        {
            //Ends at (4.5,0): only 0.5" centre-to-centre — bases overlapping. Stacking is illegal even when charging.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(4.5f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughEnemyUnit), Is.True);
        }

        [Test]
        public void ChargingOneModelWaivesStandoffForRestOfThatUnit()
        {
            //Ends in contact with enemy A at (5,0); enemy B at (5.8,0) is part of the SAME unit and ends up
            //0.8" base-to-base away (inside the standoff). Reaching A waives the standoff for B's whole unit.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0) });

            var enemyUnit = new List<EnemyModelFootprint>
            {
                new EnemyModelFootprint(new Position(5f, 0), 0.75f, unitKey: 0),
                new EnemyModelFootprint(new Position(5.8f, 0), 0.75f, unitKey: 0),
            };

            bool ok = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxRushDistance: 12f, maxDistanceInches: 12f, enemyUnit, terrain: null, out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void AlreadyInsideStandoff_MovingAway_Accepted()
        {
            //Starts inside the standoff (0.5" base-to-base) and retreats. A move that doesn't close the gap
            //must not be trapped by the standoff rule.
            DataBinding<ModelData> model = MakeModel(new Position(3, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(1, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void NoEnemyFootprints_NotRejected()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(5, 0) });

            bool ok = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxRushDistance: 12f, maxDistanceInches: 12f,
                new List<EnemyModelFootprint>(), terrain: null, out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        private static List<EnemyModelFootprint> Enemy(Position center)
            => new List<EnemyModelFootprint> { new EnemyModelFootprint(center, 0.75f, unitKey: 0) };

        private static bool Validate(ModelMoveEntry move, List<EnemyModelFootprint> enemies,
            out List<ReasonForInvalidMove> errors)
            => MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxRushDistance: 12f, maxDistanceInches: 12f, enemies, terrain: null, out errors);

        private static string Why(List<ReasonForInvalidMove> errors)
            => "Unexpected errors: " + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString()));

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
