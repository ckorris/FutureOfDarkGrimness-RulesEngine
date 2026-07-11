using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #205 — ValidateEndsOnFriendly: a move may pass THROUGH a friendly unit but may not END stacked on it.
    // Only true base overlap (interpenetration) is illegal - friendlies have no standoff band, unlike enemies -
    // and a model already overlapping a friendly at its start isn't newly penalised (so it's never trapped).
    // The GUI resolver enforced this live; these guard the authoritative engine check the AI resolvers use.
    // All models are radius 0.75", so bases touch at 1.5" centre-to-centre and overlap below that.
    [TestFixture]
    public class EndsOnFriendlyValidationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public void EndsStackedOnFriendly_Rejected()
        {
            // Ends at (4.5,0): 0.5" centre-to-centre from the friendly at (5,0) — bases overlapping.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(4.5f, 0) });

            bool ok = Validate(move, Friendly(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedOnFriendlyUnit), Is.True);
        }

        [Test]
        public void PassesThroughFriendly_EndsClear_Accepted()
        {
            // Straight line from (0,0) to (10,0) runs OVER a friendly at (5,0) but ends well clear of it.
            // Passing through a friendly is legal - only the end position is checked.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 0) });

            bool ok = Validate(move, Friendly(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void EndsInBaseContactWithFriendly_NotOverlapping_Accepted()
        {
            // Ends at (3.5,0): exactly base-to-base (gap = 0) with the friendly at (5,0). Friendlies have no
            // standoff band, and touching is not overlapping, so this is legal (a control against the enemy
            // rule, which WOULD forbid ending this close without charging).
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0) });

            bool ok = Validate(move, Friendly(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void AlreadyOverlappingFriendlyAtStart_Holds_Accepted()
        {
            // Starts overlapping the friendly (0.5" centre-to-centre) and holds. Shouldn't happen in legal
            // play, but the "only NEWLY ending stacked is illegal" guard must not trap a unit that begins
            // overlapped - otherwise it would have no legal move.
            DataBinding<ModelData> model = MakeModel(new Position(4.5f, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(4.5f, 0) });

            bool ok = Validate(move, Friendly(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void NoFriendlyFootprints_NotRejected()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(4.5f, 0) });

            bool ok = Validate(move, new List<EnemyModelFootprint>(), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void RectangularMover_WideFootprintEndsOverFriendly_FlaggedWhereBoundingCircleWouldPass()
        {
            // A 6"x1" base ending at (5,0): its 3" half-width reaches X=8, overlapping a friendly circle at
            // (8.4,0) (0.6" into it). The shape-aware end check flags it (#150) - confirming the rule works
            // for rectangular bases, not just circles.
            DataBinding<ModelData> rectModel = MakeModel(new RectangleBase(6f, 1f), new Position(0, 0));
            bool rectOk = Validate(new ModelMoveEntry(rectModel, new List<Position> { new Position(5f, 0) }),
                Friendly(new Position(8.4f, 0)), out var rectErrors);

            Assert.That(rectOk, Is.False, "the wide footprint ends overlapping the friendly.");
            Assert.That(rectErrors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedOnFriendlyUnit), Is.True);

            // Control: a circular base of the rectangle's inscribed radius (0.5") reads a clear gap and makes
            // the same move legally - the approximation the shape-aware check replaces.
            DataBinding<ModelData> circleModel = MakeModel(new CircleBase(0.5f), new Position(0, 0));
            bool circleOk = Validate(new ModelMoveEntry(circleModel, new List<Position> { new Position(5f, 0) }),
                Friendly(new Position(8.4f, 0)), out _);

            Assert.That(circleOk, Is.True, "a bounding-circle approximation would let this move pass.");
        }

        private static List<EnemyModelFootprint> Friendly(Position center)
            => new List<EnemyModelFootprint> { new EnemyModelFootprint(center, 0.75f, unitKey: 0) };

        // No enemies in play - drives the friendly-footprint parameter of the enemy-aware overload directly.
        private static bool Validate(ModelMoveEntry move, List<EnemyModelFootprint> friendlies,
            out List<ReasonForInvalidMove> errors)
            => MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f, enemyFootprints: new List<EnemyModelFootprint>(),
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false,
                terrain: null, out errors, friendlyFootprints: friendlies);

        private static string Why(List<ReasonForInvalidMove> errors)
            => "Unexpected errors: " + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString()));

        private DataBinding<ModelData> MakeModel(IBaseShape shape, Position initialPosition)
        {
            ModelData modelData = new ModelData(shape, new List<Weapon>(), initialPosition, _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
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
