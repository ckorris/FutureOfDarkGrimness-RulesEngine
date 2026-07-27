using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #291 — a model may not end a move with part of its base off the table.
    //
    // Reported from play: "vehicles are able to move partially off the table". The movement validator had
    // NO table-bounds rule at all — the only thing keeping models on the board was the GUI refusing clicks
    // outside it, which constrains a model's CENTRE. That is why it showed up on vehicles: a big base
    // overhangs the edge long before its centre leaves the table.
    //
    // The check uses the TRUE oriented footprint, not a bounding circle, so a rectangular vehicle can still
    // park flush along an edge with its long side parallel to it.
    [TestFixture]
    public class MoveStaysOnTableTests
    {
        private const float W = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
        private const float H = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // The reported case: a 4"x2" vehicle whose CENTRE is still on the table but whose base hangs over.
        [Test]
        public void AVehicleWhoseBaseOverhangsTheEdge_IsRejected_EvenThoughItsCentreIsOnTable()
        {
            DataBinding<ModelData> vehicle = MakeModel(new Position(20f, 20f),
                new RectangleBase(widthInches: 4f, heightInches: 2f));

            // Centre 0.5" inside the left edge: on the table, but 1.5" of a 4"-wide base is not.
            var move = Move(vehicle, new Position(0.5f, 20f));

            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                    maxDistanceInches: 40f, out List<ReasonForInvalidMove> errors), Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedOffTable), Is.True,
                "the centre is on the table, but the base is not - that is the reported bug");
        }

        // ...and the same vehicle fully inside is fine, so the rule isn't just "vehicles can't move".
        [Test]
        public void TheSameVehicleFullyOnTheTable_IsAccepted()
        {
            DataBinding<ModelData> vehicle = MakeModel(new Position(20f, 20f),
                new RectangleBase(widthInches: 4f, heightInches: 2f));

            var move = Move(vehicle, new Position(10f, 20f));

            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 40f, out _), Is.True);
        }

        // The reason the check uses the true footprint rather than the circumscribing circle: a 4"x2" base
        // has a 2.24" circumscribed radius, so a radius test would hold this vehicle 2.24" off the edge even
        // when its 1"-deep side is the one facing the edge.
        [Test]
        public void ARectangularBaseMayParkFlushAlongAnEdge_WhenItsShortSideFacesTheEdge()
        {
            var shape = new RectangleBase(widthInches: 4f, heightInches: 2f);
            // Default facing (0,1): local +Z (height, 2") runs along +z, width (4") spans x.
            // Centred 1.05" from the near edge, the 2"-deep base clears it with room to spare.
            DataBinding<ModelData> vehicle = MakeModel(new Position(20f, 20f), shape);
            var move = Move(vehicle, new Position(20f, 1.05f));

            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                    maxDistanceInches: 40f, out _), Is.True,
                "a circumscribing-radius test would have rejected this by over an inch");
            Assert.That(shape.CircumscribedRadiusInches, Is.GreaterThan(2f),
                "sanity: the conservative radius really is bigger than the half-depth used here");
        }

        // A rotation that turns the long side toward the edge is what makes the same centre illegal - so the
        // check is genuinely facing-aware rather than accidentally passing on axis-aligned cases.
        [Test]
        public void RotatingTheLongSideTowardTheEdge_MakesTheSamePositionIllegal()
        {
            var shape = new RectangleBase(widthInches: 4f, heightInches: 2f);
            DataBinding<ModelData> vehicle = MakeModel(new Position(20f, 20f), shape);

            // Facing (1,0): the 4" height axis now runs along +x, so 2" of it hangs past the near edge...
            var rotated = new ModelMoveEntry(vehicle,
                new List<Position> { new Position(20f, 1.05f) },
                new List<Float2> { new Float2(0f, 1f) });
            var rotatedIntoEdge = new ModelMoveEntry(vehicle,
                new List<Position> { new Position(1.05f, 20f) },
                new List<Float2> { new Float2(0f, 1f) });

            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { rotated },
                    maxDistanceInches: 40f, out _), Is.True,
                "short side to the edge: legal");
            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { rotatedIntoEdge },
                    maxDistanceInches: 40f, out _), Is.False,
                "long side to the edge at the same clearance: not legal");
        }

        [TestCase(0f, 20f, TestName = "OffTable_LeftEdge")]
        [TestCase(20f, 0f, TestName = "OffTable_NearEdge")]
        [TestCase(W, 20f, TestName = "OffTable_RightEdge")]
        [TestCase(20f, H, TestName = "OffTable_FarEdge")]
        public void ACircularBaseCentredExactlyOnAnEdge_IsRejected(float x, float z)
        {
            DataBinding<ModelData> infantry = MakeModel(new Position(20f, 20f), new CircleBase(0.75f));

            var move = Move(infantry, new Position(x, z));

            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                    maxDistanceInches: 40f, out List<ReasonForInvalidMove> errors), Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedOffTable), Is.True);
        }

        // Not an absolute rule but a "not worsened" one, matching ValidateEndsOnFriendly /
        // ValidateCoherencyNotWorsened: a model that somehow ALREADY overhangs must not be frozen in place by
        // a validator that rejects every move available to it. Pulling in is always legal; sliding further
        // out is not.
        [Test]
        public void AModelThatAlreadyOverhangs_MayMoveBackOn_ButNotFurtherOut()
        {
            var shape = new CircleBase(1f);
            DataBinding<ModelData> stranded = MakeModel(new Position(0.25f, 20f), shape); // 0.75" over the edge

            var pullingIn = Move(stranded, new Position(0.6f, 20f));   // still over, but less
            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { pullingIn },
                    maxDistanceInches: 40f, out _), Is.True,
                "a move that reduces the overhang must stay legal, or the model is trapped forever");

            var fullyOn = Move(stranded, new Position(5f, 20f));
            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { fullyOn },
                maxDistanceInches: 40f, out _), Is.True);

            var slidingOut = Move(stranded, new Position(0.1f, 20f));
            Assert.That(MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { slidingOut },
                    maxDistanceInches: 40f, out _), Is.False,
                "but making it worse is still rejected");
        }

        // Consolidation runs its own validator, and the same rule has to apply - a disengage move is exactly
        // the kind of small nudge that would otherwise slide a big base off an edge.
        [Test]
        public void ConsolidationMovesAreBoundedToo()
        {
            DataBinding<ModelData> vehicle = MakeModel(new Position(1.5f, 20f),
                new RectangleBase(widthInches: 4f, heightInches: 2f));

            var move = Move(vehicle, new Position(0.6f, 20f));

            Assert.That(MovementUtilities.ValidateConsolidationPaths(new List<ModelMoveEntry> { move },
                    maxDistanceInches: 3f, enemyFootprints: Array.Empty<EnemyModelFootprint>(),
                    canMoveThroughEnemies: false, ignoresDifficultTerrain: false,
                    ignoresImpassibleTerrain: false, terrain: null,
                    out List<ReasonForInvalidMove> errors), Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedOffTable), Is.True);
        }

        // The measurement itself, stated directly - the seam every case above leans on.
        [Test]
        public void OverhangInches_IsZeroInside_AndTheDistancePastTheEdgeOutside()
        {
            var circle = new CircleBase(1f);
            Assert.That(MovementUtilities.OverhangInches(circle, new Position(20f, 20f), new Float2(0f, 1f)),
                Is.EqualTo(0f).Within(1e-4f));
            Assert.That(MovementUtilities.OverhangInches(circle, new Position(0.25f, 20f), new Float2(0f, 1f)),
                Is.EqualTo(0.75f).Within(1e-4f), "a 1\" base centred 0.25\" in hangs 0.75\" over");
            Assert.That(MovementUtilities.OverhangInches(circle, new Position(1f, 20f), new Float2(0f, 1f)),
                Is.EqualTo(0f).Within(1e-4f), "exactly flush is on the table");
        }

        // The resolvers' side of the rule: the ghost has to STOP at the edge, or the player would only find
        // out at Done - where an invalid path throws rather than being politely refused.
        [Test]
        public void ClampTravelToTable_StopsExactlyAtTheEdge()
        {
            var shape = new CircleBase(1f);
            // Heading for the left edge from 10" in, asking for 20": it may travel 9" (centre lands at 1").
            float allowed = MovementUtilities.ClampTravelToTable(new Position(10f, 20f),
                dirX: -1f, dirZ: 0f, allowedInches: 20f, shape, new Float2(0f, 1f));

            Assert.That(allowed, Is.EqualTo(9f).Within(0.001f));
            Assert.That(MovementUtilities.OverhangInches(shape, new Position(10f - allowed, 20f), new Float2(0f, 1f)),
                Is.EqualTo(0f).Within(0.001f), "and the point it stops at is genuinely on the table");
        }

        [Test]
        public void ClampTravelToTable_LeavesAMoveThatStaysInsideAlone()
        {
            float allowed = MovementUtilities.ClampTravelToTable(new Position(20f, 20f),
                dirX: -1f, dirZ: 0f, allowedInches: 5f, new CircleBase(1f), new Float2(0f, 1f));

            Assert.That(allowed, Is.EqualTo(5f).Within(1e-4f), "no clamping when the whole step fits");
        }

        // A big base is stopped further out than a small one from the same spot - the vehicle case, as a
        // travel budget rather than a verdict.
        [Test]
        public void ClampTravelToTable_StopsABigBaseSoonerThanASmallOne()
        {
            float small = MovementUtilities.ClampTravelToTable(new Position(10f, 20f), -1f, 0f, 20f,
                new CircleBase(0.5f), new Float2(0f, 1f));
            float big = MovementUtilities.ClampTravelToTable(new Position(10f, 20f), -1f, 0f, 20f,
                new RectangleBase(widthInches: 4f, heightInches: 2f), new Float2(0f, 1f));

            Assert.That(small, Is.EqualTo(9.5f).Within(0.001f));
            Assert.That(big, Is.EqualTo(8f).Within(0.001f), "the 4\"-wide base runs out 2\" from the edge");
        }

        // A stranded model must still be able to move: the clamp allows anything that doesn't worsen the
        // overhang, mirroring the validator, so the GUI can never propose what the engine would reject.
        [Test]
        public void ClampTravelToTable_LetsAnAlreadyOverhangingModelMoveAlongTheEdge()
        {
            var shape = new CircleBase(1f);
            float allowed = MovementUtilities.ClampTravelToTable(new Position(0.25f, 20f),
                dirX: 0f, dirZ: 1f, allowedInches: 5f, shape, new Float2(0f, 1f));

            Assert.That(allowed, Is.EqualTo(5f).Within(1e-4f),
                "sliding parallel to the edge doesn't deepen the overhang, so it stays available");
        }

        private static ModelMoveEntry Move(DataBinding<ModelData> model, Position to) =>
            new ModelMoveEntry(model, new List<Position> { to });

        private DataBinding<ModelData> MakeModel(Position at, IBaseShape shape)
        {
            var model = new ModelData(shape, new List<Weapon>(), at, _store);
            DataBinding<ModelData> binding = _store.GetDataBinding<ModelData>(_store.Create(model));

            // A unit + army so cohesion/ownership checks have something to read; one model means the
            // cohesion rules are trivially satisfied and only the bounds rule can fail these moves.
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Vehicle", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { binding });
            _store.Create(new ArmyData(unit.PlayerID, new List<DataBinding<UnitData>>
                { _store.GetDataBinding<UnitData>(_store.Create(unit)) }));
            return binding;
        }
    }
}
