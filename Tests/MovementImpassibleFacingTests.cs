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

        // The detailed finder behind the preview's "show me why" overlay: same verdict as the boolean, plus
        // WHICH piece, WHICH segment, and the centre at first contact - so the resolver can point at the
        // collision instead of leaving a red flag on visibly open ground.
        [Test]
        public void FindFirstCrossing_ReportsPieceSegmentAndContact()
        {
            // Resting +X and travelling +X, so the leg's two endpoint attitudes agree and #340's either-attitude
            // rule collapses to the single sweep this has always tested: a mid-leg contact found by bisection.
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), restFacing: new Float2(1f, 0f));
            var move = TravelMove(model, new Position(3f, 0f));
            List<ITerrain> band = ImpassibleBand();

            MovementUtilities.TerrainCrossing? crossing =
                MovementUtilities.FindFirstImpassibleCrossing(move, band);

            Assert.That(crossing, Is.Not.Null);
            Assert.That(crossing!.Value.Piece, Is.SameAs(band[0]));
            Assert.That(crossing.Value.SegmentIndex, Is.EqualTo(0));
            // Facing +X leads with the 3" half-height, so the footprint first touches the X=4 band edge
            // when the centre reaches X=1 (bisection-accurate contact point).
            Assert.That(crossing.Value.ContactCentre.X, Is.EqualTo(1f).Within(0.02f));
            Assert.That(crossing.Value.ContactCentre.Y, Is.EqualTo(0f).Within(0.02f));
        }

        // #340: the same destination reached from a resting +Z facing is a NODE-POSE collision, not a leg one.
        // Swept at the resting attitude the 0.5" half-width stays short of the band all the way to X=3, so the
        // travel is legal - what is not is standing there turned +X, which puts the 3" nose inside it. Reported
        // as a zero-length segment AT the node, which is what the "show me why" overlay needs to draw.
        [Test]
        public void FindFirstCrossing_RotatedIntoTerrainOnArrival_ReportsTheNodePose()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), restFacing: new Float2(0f, 1f));
            var move = TravelMove(model, new Position(3f, 0f));

            MovementUtilities.TerrainCrossing? crossing =
                MovementUtilities.FindFirstImpassibleCrossing(move, ImpassibleBand());

            Assert.That(crossing, Is.Not.Null, "the arrival pose stands the long axis in the band");
            Assert.That(crossing!.Value.SegmentIndex, Is.EqualTo(0));
            Assert.That(crossing.Value.SegmentStart.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(crossing.Value.SegmentEnd.X, Is.EqualTo(3f).Within(0.001f),
                "a node pose is reported as a zero-length segment at the node");
            Assert.That(crossing.Value.ContactCentre.X, Is.EqualTo(3f).Within(0.001f));
        }

        // The TankCantMakeCorner mechanism, after #340: a rotation dialled in for a later node no longer
        // re-orients the leg that led up to it. The first leg is walked at the attitude the model DEPARTED
        // with and stays clear; what the rotation can still cost you is the pose at the node itself.
        [Test]
        public void FindFirstCrossing_LateRotationOffset_LeavesTheEarlierLegAlone()
        {
            // 1"x6" base (0.5" half-width, 3" half-height) driving +Z past a pillar at X 2..3, Z 0..4. Facing
            // along travel the footprint reaches only X=0.5, so leg 0 skirts the pillar. A 45deg hand offset
            // swings the 3" half-height toward it (reach = 0.5cos45 + 3sin45 ~ 2.47").
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), restFacing: new Float2(0f, 1f));
            var pillar = new List<ITerrain>
                { new TerrainData(ETerrainType.Impassible, new RectangularZone(2f, 3f, 0f, 4f)) };

            // Turning at a node level with the pillar puts the swung long axis inside it - still rejected, and
            // attributed to that node rather than to open ground the cursor is nowhere near.
            var alongside = new List<Position> { new Position(0f, 6f), new Position(0f, 12f) };
            MovementUtilities.TerrainCrossing? blocked = MovementUtilities.FindFirstImpassibleCrossing(
                OffsetMove(model, alongside, offsetRadians: MathF.PI / 4f), pillar);
            Assert.That(blocked, Is.Not.Null, "the 45deg pose at the node swings the long axis into the pillar");
            Assert.That(blocked!.Value.SegmentIndex, Is.EqualTo(0));
            Assert.That(blocked.Value.SegmentStart.Y, Is.EqualTo(6f).Within(0.001f),
                "attributed to the node that is turned, not to the leg that got there");

            // Turn well past the pillar instead and the whole path is legal: leg 0 runs at the resting attitude
            // (which skirts it), and the rotated pose at Z=20 is nowhere near. Before #340 the offset was applied
            // to leg 0 as well and this move was refused.
            var pastIt = new List<Position> { new Position(0f, 20f), new Position(0f, 26f) };
            Assert.That(MovementUtilities.FindFirstImpassibleCrossing(
                    OffsetMove(model, pastIt, offsetRadians: MathF.PI / 4f), pillar), Is.Null,
                "a rotation placed past the pillar does not reach back and re-orient the leg before it");
        }

        // ---------------------------------------------------------------------------------------------
        // #340 - the owner's 2026-08-04 report: a rectangular model parked beside a wall could not be told
        // to "move out a way and THEN turn", because the turn was applied to the square it was standing on.
        // ---------------------------------------------------------------------------------------------

        // A wall running up the model's west side and ENDING at Z=4, so there is clear ground beyond it.
        private static List<ITerrain> WallToTheWest()
            => new List<ITerrain> { new TerrainData(ETerrainType.Impassible, new RectangularZone(-2f, 0f, -4f, 4f)) };

        // The model hugging that wall: a 1"x6" base at X=0.6 facing along it (+Z), 0.1" of daylight to the wall.
        private DataBinding<ModelData> ModelHuggingTheWall()
            => MakeModel(new Position(0.6f, 0f), restFacing: new Float2(0f, 1f));

        [Test]
        public void MoveOutThenTurn_IsLegal_ThoughTheTurnWouldNotFitAtTheStartSquare()
        {
            // Drive +Z clear of the wall's Z=4 end, arriving turned 90deg to +X. At that arrival attitude the
            // 3" half-height reaches X=-2.4 - which, applied to the START square, is 0.4" inside the wall. That
            // is exactly what used to refuse this move.
            DataBinding<ModelData> model = ModelHuggingTheWall();
            var move = OffsetMove(model, new List<Position> { new Position(0.6f, 8f) },
                offsetRadians: -MathF.PI / 2f);

            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(move, WallToTheWest()), Is.False,
                "the leg is clear at the attitude the model departed with; the turn happens on arrival");

            bool ok = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f, NoEnemies(), canMoveThroughEnemies: false,
                ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false, WallToTheWest(),
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True, "the authoritative gate must agree with the preview: " + Describe(errors));
        }

        [Test]
        public void TurningIntoTheWall_IsStillRejected()
        {
            // The guard rail: the same 90deg turn, but taken while still ALONGSIDE the wall. The leg is clear
            // (the model travels at its departing attitude), so only the node-pose check can catch this - and
            // must, or the move rule would have traded one bug for a worse one.
            DataBinding<ModelData> model = ModelHuggingTheWall();
            var move = OffsetMove(model, new List<Position> { new Position(0.6f, 2f) },
                offsetRadians: -MathF.PI / 2f);

            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(move, WallToTheWest()), Is.True,
                "the arrival pose puts the 3\" nose inside the wall");

            bool ok = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f, NoEnemies(), canMoveThroughEnemies: false,
                ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false, WallToTheWest(),
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain), Is.True);
        }

        [Test]
        public void AutoTravelFacing_NoLongerCollidesAtTheStartSquare()
        {
            // No keypress needed to hit the same defect: #150 auto-faces each waypoint along its direction of
            // travel, so a rectangle parked parallel to the wall that sets off diagonally is turned toward the
            // wall - and the old single-attitude sweep applied that turn to the square it was leaving, where
            // the swung corner sits at X=-1.8, inside the wall.
            DataBinding<ModelData> model = ModelHuggingTheWall();
            var move = TravelMove(model, new Position(8f, 8f));

            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(move, WallToTheWest()), Is.False,
                "the diagonal departure is clear at the resting attitude it sets off in");
        }

        private static string Describe(List<ReasonForInvalidMove> errors)
            => string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString()));

        private ModelMoveEntry OffsetMove(DataBinding<ModelData> model, List<Position> waypoints, float offsetRadians)
        {
            List<Float2> facings = MovementFacingUtilities.WaypointFacings(
                model.GetValue().Position, waypoints, model.GetValue().Facing, offsetRadians);
            return new ModelMoveEntry(model, waypoints, facings);
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
