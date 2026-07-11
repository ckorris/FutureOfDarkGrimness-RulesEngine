using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #155: the difficult-terrain move-preview clamp — the enforcement mirror of the
    // ExceededDifficultTerrainMoveLimit validator, used by resolvers to stop a live ghost from ever
    // drawing a move the validator would reject. Crossing Difficult terrain caps the TOTAL move at
    // DIFFICULT_TERRAIN_MOVE_CAP_INCHES (6"); a model that can no longer afford to enter stops just
    // short of the terrain edge instead.
    [TestFixture]
    public class DifficultTerrainClampTests
    {
        private const float Cap = GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES;
        private const float Margin = MovementUtilities.DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES;

        private static readonly Float2 FacingZ = new Float2(0f, 1f);
        private static readonly CircleBase HalfInchBase = new CircleBase(0.5f);

        // One difficult band at X 2–10 (tall enough that a +X path at z=0 must cross it).
        private static List<ITerrain> DifficultBand() => new List<ITerrain>
        {
            new TerrainData(ETerrainType.Difficult, new RectangularZone(2f, 10f, -2f, 2f))
        };

        [Test]
        public void NoDifficultPieces_FullSegmentAllowed()
        {
            var coverOnly = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Cover, new RectangularZone(2f, 10f, -2f, 2f))
            };

            float allowed = MovementUtilities.ClampTravelForDifficultTerrain(
                new Float2(0f, 0f), new Float2(12f, 0f), traveledBeforeSegmentInches: 0f,
                pathAlreadyCrossedDifficultTerrain: false, HalfInchBase, FacingZ,
                coverOnly, ignoresDifficultTerrain: false);

            Assert.That(allowed, Is.EqualTo(12f));
        }

        [Test]
        public void IgnoresDifficultTerrain_FullSegmentAllowed()
        {
            float allowed = MovementUtilities.ClampTravelForDifficultTerrain(
                new Float2(0f, 0f), new Float2(12f, 0f), traveledBeforeSegmentInches: 0f,
                pathAlreadyCrossedDifficultTerrain: false, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: true);

            Assert.That(allowed, Is.EqualTo(12f));
        }

        [Test]
        public void SegmentMissesDifficult_FullSegmentAllowed()
        {
            // Path at z=5 stays clear of the band (top edge z=2, base radius 0.5).
            float allowed = MovementUtilities.ClampTravelForDifficultTerrain(
                new Float2(0f, 5f), new Float2(12f, 5f), traveledBeforeSegmentInches: 0f,
                pathAlreadyCrossedDifficultTerrain: false, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: false);

            Assert.That(allowed, Is.EqualTo(12f));
        }

        [Test]
        public void AlreadyCrossed_SegmentGetsRemainingCapMinusMargin()
        {
            // 4" already travelled through/past difficult: 6 - 4 - margin remains, direction irrelevant.
            float allowed = MovementUtilities.ClampTravelForDifficultTerrain(
                new Float2(0f, 5f), new Float2(12f, 5f), traveledBeforeSegmentInches: 4f,
                pathAlreadyCrossedDifficultTerrain: true, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: false);

            Assert.That(allowed, Is.EqualTo(Cap - 4f - Margin).Within(0.001f));
        }

        [Test]
        public void AlreadyCrossed_PastCap_NothingAllowed()
        {
            float allowed = MovementUtilities.ClampTravelForDifficultTerrain(
                new Float2(0f, 5f), new Float2(12f, 5f), traveledBeforeSegmentInches: 7f,
                pathAlreadyCrossedDifficultTerrain: true, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: false);

            Assert.That(allowed, Is.EqualTo(0f));
        }

        [Test]
        public void EntersBeforeCap_TotalMoveCappedAtSix()
        {
            // Entry at travel 1.5 (disc edge meets X=2). The cap still leaves room past the entry, so it
            // becomes the limit: 6 - margin of the desired 12.
            float allowed = MovementUtilities.ClampTravelForDifficultTerrain(
                new Float2(0f, 0f), new Float2(12f, 0f), traveledBeforeSegmentInches: 0f,
                pathAlreadyCrossedDifficultTerrain: false, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: false);

            Assert.That(allowed, Is.EqualTo(Cap - Margin).Within(0.001f));
        }

        [Test]
        public void EntryPastCap_StopsJustShortOfTerrainEdge()
        {
            // 5.5" already travelled (difficult not yet crossed): entering at 1.5 would put the total at 7,
            // past the cap — so the segment stops just short of the edge instead.
            float allowed = MovementUtilities.ClampTravelForDifficultTerrain(
                new Float2(0f, 0f), new Float2(12f, 0f), traveledBeforeSegmentInches: 5.5f,
                pathAlreadyCrossedDifficultTerrain: false, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: false);

            Assert.That(allowed, Is.EqualTo(1.5f - Margin).Within(0.02f));
            // The clamped endpoint must not touch the zone.
            Assert.That(SweptBaseGeometry.DoesSweptBaseIntersectZone(
                DifficultBand()[0].Shape, new Float2(0f, 0f), new Float2(allowed, 0f), HalfInchBase, FacingZ),
                Is.False);
        }

        [Test]
        public void ClampedMove_PassesTheAuthoritativeValidator()
        {
            // End-to-end agreement: clamp a segment that crosses difficult, then validate the resulting
            // waypoint with the real validator — it must come back clean.
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ModelData modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0),
                gameDataStore: store);
            DataBinding<ModelData> model = store.GetDataBinding<ModelData>(store.Create(modelData));

            List<ITerrain> terrain = DifficultBand();
            IModel m = model.GetValue();
            float allowed = MovementUtilities.ClampTravelForDifficultTerrain(
                new Float2(0f, 0f), new Float2(12f, 0f), traveledBeforeSegmentInches: 0f,
                pathAlreadyCrossedDifficultTerrain: false, m.BaseShape, m.Facing,
                terrain, ignoresDifficultTerrain: false);

            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(allowed, 0) });
            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move }, maxDistanceInches: 12f, terrain, out var errors);

            Assert.That(ok, Is.True, string.Join(", ", errors.Select(e => e.ErrorReasonType)));
        }

        [Test]
        public void Detailed_ReportsCappedCrossing_WhenEnteringWithCapRoom()
        {
            var r = MovementUtilities.ClampTravelForDifficultTerrainDetailed(
                new Float2(0f, 0f), new Float2(12f, 0f), traveledBeforeSegmentInches: 0f,
                pathAlreadyCrossedDifficultTerrain: false, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: false);

            Assert.That(r.Kind, Is.EqualTo(MovementUtilities.EDifficultClampKind.CappedCrossing));
            Assert.That(r.AllowedInches, Is.EqualTo(Cap - Margin).Within(0.001f));
        }

        [Test]
        public void Detailed_ReportsStoppedShortOfEdge_WhenEntryUnaffordable()
        {
            var r = MovementUtilities.ClampTravelForDifficultTerrainDetailed(
                new Float2(0f, 0f), new Float2(12f, 0f), traveledBeforeSegmentInches: 5.5f,
                pathAlreadyCrossedDifficultTerrain: false, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: false);

            Assert.That(r.Kind, Is.EqualTo(MovementUtilities.EDifficultClampKind.StoppedShortOfEdge));
            Assert.That(r.AllowedInches, Is.EqualTo(1.5f - Margin).Within(0.02f));
        }

        [Test]
        public void Detailed_ReportsNotLimited_WhenSegmentMissesDifficult()
        {
            var r = MovementUtilities.ClampTravelForDifficultTerrainDetailed(
                new Float2(0f, 5f), new Float2(12f, 5f), traveledBeforeSegmentInches: 0f,
                pathAlreadyCrossedDifficultTerrain: false, HalfInchBase, FacingZ,
                DifficultBand(), ignoresDifficultTerrain: false);

            Assert.That(r.Kind, Is.EqualTo(MovementUtilities.EDifficultClampKind.NotLimited));
            Assert.That(r.AllowedInches, Is.EqualTo(12f));
        }

        [Test]
        public void DoesPathCrossDifficultTerrain_MirrorsTheDangerousCheck()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ModelData modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0),
                gameDataStore: store);
            DataBinding<ModelData> model = store.GetDataBinding<ModelData>(store.Create(modelData));

            List<ITerrain> terrain = DifficultBand();

            ModelMoveEntry crossing = new ModelMoveEntry(model, new List<Position> { new Position(12, 0) });
            Assert.That(MovementUtilities.DoesPathCrossDifficultTerrain(crossing, terrain), Is.True);

            // Dangerous-flagged pieces must not register as difficult.
            var dangerousOnly = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Dangerous, new RectangularZone(2f, 10f, -2f, 2f))
            };
            Assert.That(MovementUtilities.DoesPathCrossDifficultTerrain(crossing, dangerousOnly), Is.False);

            ModelMoveEntry stationary = new ModelMoveEntry(model, new List<Position>());
            Assert.That(MovementUtilities.DoesPathCrossDifficultTerrain(stationary, terrain), Is.False);
        }

        // #213: the impassible preview sibling of the Difficult/Dangerous checks - lets the move GUI flag a
        // path that would move THROUGH impassible terrain as invalid (red, un-clickable) up front.
        [Test]
        public void DoesPathCrossImpassibleTerrain_MirrorsTheDifficultCheck()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ModelData modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0),
                gameDataStore: store);
            DataBinding<ModelData> model = store.GetDataBinding<ModelData>(store.Create(modelData));

            var impassible = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new RectangularZone(2f, 10f, -2f, 2f))
            };

            // A straight run along z=0 sweeps through the wall at x in [2,10].
            ModelMoveEntry crossing = new ModelMoveEntry(model, new List<Position> { new Position(12, 0) });
            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(crossing, impassible), Is.True);

            // Difficult-flagged pieces must not register as impassible.
            var difficultOnly = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(2f, 10f, -2f, 2f))
            };
            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(crossing, difficultOnly), Is.False);

            // A path that stays clear of the wall (up the x=0 lane) doesn't cross it.
            ModelMoveEntry clear = new ModelMoveEntry(model, new List<Position> { new Position(0, 10) });
            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(clear, impassible), Is.False);

            ModelMoveEntry stationary = new ModelMoveEntry(model, new List<Position>());
            Assert.That(MovementUtilities.DoesPathCrossImpassibleTerrain(stationary, impassible), Is.False);
        }
    }
}
