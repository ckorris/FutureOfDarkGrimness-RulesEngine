using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #150 follow-up: the swept-base impassible-terrain check must sweep each segment at the facing the model
    // will actually HAVE while traversing it - its per-waypoint travel facing - not the model's pre-move
    // resting facing. For a large non-square base the orientation decides which footprint the SAT sweep uses,
    // so a frozen resting facing let the same destination read valid/invalid depending only on which way the
    // model happened to be pointing before the move (the "awkward movement" a Heavy Skimmer showed). Both the
    // preview helper (DoesPathCrossImpassibleTerrain) and the authoritative gate (ValidatePaths) must agree.
    [TestFixture]
    public class MovementImpassibleFacingTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // A 1"x6" base (0.5" half-width, 3" half-height) driving +X toward an impassible band at X 4..6. Facing
        // +X leads with the 3" half-height (reaches X=4 while the centre is still at X=1 -> blocked); facing +Z
        // leads with the 0.5" half-width (a centre stopping at X=3 keeps the footprint short of X=4 -> clear).
        private static readonly RectangleBase LongBase = new RectangleBase(1f, 6f);
        // A 6"x1" base: its long axis runs PERPENDICULAR to facing (width 6, half 3), so it leads with the long
        // axis when facing across the direction of travel - the mirror case that distinguishes travel from rest.
        private static readonly RectangleBase WideBase = new RectangleBase(6f, 1f);
        private static List<ITerrain> ImpassibleBand()
            => new List<ITerrain> { new TerrainData(ETerrainType.Impassible, new RectangularZone(4f, 6f, -10f, 10f)) };

        [Test]
        public void PreviewCheck_TravelFacing_BlocksWhereRestingFacingWouldMiss()
        {
            // Model rests facing +Z but the move drives it +X; its travel facing turns the long axis toward the
            // band, so a 3" push that ends at X=3 sweeps the 3" half-height into the X=4 edge -> crosses.
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), restFacing: new Float2(0f, 1f));
            var move = TravelMove(model, new Position(3f, 0f));

            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(move, ImpassibleBand()), Is.True,
                "swept at the +X travel facing the long axis reaches the band");
        }

        [Test]
        public void PreviewCheck_TravelFacing_ClearsWhereRestingFacingWouldBlock()
        {
            // The mirror case: a 6"x1" base rests facing +Z (its long width-axis lies along +X, reaching X=3
            // from the centre) but the move drives it +X. Its travel facing turns to +X, swinging the long axis
            // OFF the lane so only the 0.5" leading half stays ahead; a 3" push ends at X=3, short of the X=4 band
            // -> clear. Swept at the resting facing the long axis would have reached X=6 and read as blocked.
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), restFacing: new Float2(0f, 1f), shape: WideBase);
            var move = TravelMove(model, new Position(3f, 0f));

            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(move, ImpassibleBand()), Is.False,
                "swept at the +X travel facing the long axis swings off the lane and the base stops short");
        }

        [Test]
        public void AuthoritativeGate_TravelFacing_RejectsMoveThroughImpassible()
        {
            // The Done-gate path (ValidatePaths -> ValidateMovingThroughImpassibleTerrain) must reach the same
            // verdict as the preview: a rest-+Z model driven +X into the band is rejected on its travel facing.
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), restFacing: new Float2(0f, 1f));
            var move = TravelMove(model, new Position(3f, 0f));

            bool ok = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f, NoEnemies(), canMoveThroughEnemies: false,
                ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false, ImpassibleBand(),
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain), Is.True);
        }

        [Test]
        public void NoFacings_FallsBackToRestingFacing_Unchanged()
        {
            // A move with no per-waypoint facings (AI / consolidation / executor holds) must behave exactly as
            // before: swept at the model's resting facing. Rest +X, band along the +X lane -> long axis crosses.
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), restFacing: new Float2(1f, 0f));
            var move = new ModelMoveEntry(model, new List<Position> { new Position(3f, 0f) }); // Facings == null

            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(move, ImpassibleBand()), Is.True,
                "with no travel facings the resting +X facing sweeps the long axis into the band");
        }

        private static List<EnemyModelFootprint> NoEnemies() => new List<EnemyModelFootprint>();

        // A single-waypoint move whose facing follows the direction of travel (the GUI resolver's default).
        private ModelMoveEntry TravelMove(DataBinding<ModelData> model, Position to)
        {
            var waypoints = new List<Position> { to };
            List<Float2> facings = MovementFacingUtilities.WaypointFacings(
                model.GetValue().Position, waypoints, model.GetValue().Facing, 0f);
            return new ModelMoveEntry(model, waypoints, facings);
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition, Float2 restFacing, IBaseShape? shape = null)
        {
            ModelData modelData = new ModelData(shape ?? LongBase, new List<Weapon>(), initialPosition, _store);
            modelData.SetFacing(restFacing);
            return _store.GetDataBinding<ModelData>(_store.Create(modelData));
        }
    }
}
