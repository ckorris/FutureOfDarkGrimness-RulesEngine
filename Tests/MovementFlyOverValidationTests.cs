using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #090 — the Strafing/fly-over exemption to #011's enemy move-through check, and the capability query
    // that drives it. A fly-over unit (canMoveThroughEnemies) may PATH through an enemy base but still may
    // not END a move stacked on one. Models are radius 0.75" (base contact at 1.5" centre-to-centre).
    [TestFixture]
    public class MovementFlyOverValidationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void PathThroughEnemy_BlockedWithoutFlyOver()
        {
            // Control: straight line (0,0)→(10,0) runs over an enemy at (5,0).
            ModelMoveEntry move = Move(new Position(0, 0), new Position(10, 0));

            bool ok = Charge(move, canMoveThroughEnemies: false, out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughEnemyUnit), Is.True);
        }

        [Test]
        public void PathThroughEnemy_AllowedWithFlyOver()
        {
            // Same through-enemy path, but the mover may fly over (Strafing).
            ModelMoveEntry move = Move(new Position(0, 0), new Position(10, 0));

            bool ok = Charge(move, canMoveThroughEnemies: true, out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void FlyOver_CannotEndStackedOnEnemy()
        {
            // Ends at (4.5,0): 0.5" centre-to-centre — bases overlapping. Flying over does not permit
            // FINISHING on top of an enemy.
            ModelMoveEntry move = Move(new Position(0, 0), new Position(4.5f, 0));

            bool ok = Charge(move, canMoveThroughEnemies: true, out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughEnemyUnit), Is.True);
        }

        [Test]
        public void NoChargeOverload_ThroughEnemy_HonorsFlyOver()
        {
            // The consolidation / executor overload (no charge semantics) applies the same exemption.
            ModelMoveEntry move = Move(new Position(0, 0), new Position(10, 0));

            bool blocked = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f, Enemy(new Position(5, 0)), canMoveThroughEnemies: false,
                ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false, terrain: null, out _);
            bool allowed = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f, Enemy(new Position(5, 0)), canMoveThroughEnemies: true,
                ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false, terrain: null, out var errors);

            Assert.That(blocked, Is.False, "Without fly-over the consolidation move through an enemy is rejected.");
            Assert.That(allowed, Is.True, Why(errors));
        }

        [Test]
        public void CanMoveThroughEnemies_TrueForStrafingUnit()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(withStrafing: true);

            Assert.That(MovementRuleQueries.CanMoveThroughEnemies(unit.GetValue(), ctx.RuleEvaluator), Is.True);
        }

        [Test]
        public void CanMoveThroughEnemies_FalseForPlainUnit()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(withStrafing: false);

            Assert.That(MovementRuleQueries.CanMoveThroughEnemies(unit.GetValue(), ctx.RuleEvaluator), Is.False);
        }

        private static List<EnemyModelFootprint> Enemy(Position center)
            => new List<EnemyModelFootprint> { new EnemyModelFootprint(center, 0.75f, unitKey: 0) };

        private ModelMoveEntry Move(Position from, Position to)
            => new ModelMoveEntry(MakeModel(from), new List<Position> { to });

        private bool Charge(ModelMoveEntry move, bool canMoveThroughEnemies, out List<ReasonForInvalidMove> errors)
            => MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxRushDistance: 12f, maxDistanceInches: 12f, Enemy(new Position(5, 0)),
                canMoveThroughEnemies, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false, terrain: null, out errors);

        private static string Why(List<ReasonForInvalidMove> errors)
            => "Unexpected errors: " + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString()));

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            ModelData modelData = new ModelData(0.75f, new List<Weapon>(), initialPosition, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(modelData));
        }

        private DataBinding<UnitData> MakeUnit(bool withStrafing)
        {
            var modelBindings = new List<DataBinding<ModelData>> { MakeModel(new Position(0, 0)) };
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (withStrafing)
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Strafing", CoreRuleCatalog.Strafing));
            return binding;
        }
    }
}
