using System;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #029 — the Aircraft forced-movement helper: heading init (toward the table centre, set once and never
    // turned), rigid straight-line paths, and off-table detection.
    [TestFixture]
    public class ForcedAircraftMoveTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void EnsureHeading_PointsTowardTableCentre_AndIsStoredAsAUnitVector()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 10));

            Float2 heading = ForcedAircraftMove.EnsureHeading(unit.GetValue());

            // Table centre is (36, 24); from (10,10) the direction is +x,+z.
            Assert.That(heading.X, Is.GreaterThan(0f));
            Assert.That(heading.Y, Is.GreaterThan(0f));
            Assert.That(MathF.Sqrt(heading.X * heading.X + heading.Y * heading.Y), Is.EqualTo(1f).Within(0.001f),
                "the heading is a unit vector.");
            Assert.That(unit.GetValue().AircraftHeading, Is.EqualTo(heading), "the heading is stored on the unit.");
        }

        [Test]
        public void EnsureHeading_RespectsAnAlreadySetHeading_NeverTurns()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 10));
            unit.GetValue().AircraftHeading = new Float2(1f, 0f);

            Float2 heading = ForcedAircraftMove.EnsureHeading(unit.GetValue());

            Assert.That(heading, Is.EqualTo(new Float2(1f, 0f)),
                "an Aircraft never turns — an already-set heading is not recomputed.");
        }

        [Test]
        public void BuildPaths_TranslatesEveryModelByHeadingTimesDistance()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 10));

            System.Collections.Generic.List<ModelMoveEntry> paths =
                ForcedAircraftMove.BuildPaths(unit, new Float2(0f, 1f), 30f);

            Assert.That(paths.Count, Is.EqualTo(1));
            Position dest = paths[0].Positions[0];
            Assert.That(dest.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(dest.z, Is.EqualTo(40f).Within(0.001f), "moved 30\" straight along +z.");
        }

        [Test]
        public void WouldLeaveTable_FalseWhenInBounds_TrueOverTheEdge()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 10));

            var inBounds = ForcedAircraftMove.BuildPaths(unit, new Float2(0f, 1f), 30f);  // -> (10,40); table H=48
            Assert.That(ForcedAircraftMove.WouldLeaveTable(inBounds), Is.False);

            var offEdge = ForcedAircraftMove.BuildPaths(unit, new Float2(0f, 1f), 40f);   // -> (10,50) past the 48 edge
            Assert.That(ForcedAircraftMove.WouldLeaveTable(offEdge), Is.True);
        }

        // Integration: drive DefinePathStage with an Aircraft and confirm the forced move's two outcomes.
        // (OnPathDefined is dropped by the NoOpLayer, so the on-table move is SUBMITTED but its downstream commit
        // doesn't run — the off-table branch, which mutates models directly, is the one fully observable here.)
        [Test]
        public async Task AircraftAdvance_OffTableEdge_LeavesPlayAndMarksForRedeploy()
        {
            var ctx = new TriggeredMoveTestContext(_store, new FirstStringRequester());

            DataBinding<UnitData> aircraft = MakeUnit(new Position(10, 40)); // near the top edge (H = 48)
            aircraft.GetValue().AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));
            aircraft.GetValue().AircraftHeading = new Float2(0f, 1f); // flying +z, straight off the top edge

            var moveCtx = new MovementActionContext(ctx, aircraft);
            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("done");
            await stage.Enter(moveCtx);

            Position p = aircraft.GetValue().Models[0].Position;
            Assert.That(p.x == 0f && p.z == 0f, Is.True, "flew off the table — held off-table (models at origin).");
            Assert.That(aircraft.GetValue().Tokens.HasToken(TokenType.OffTableFromForcedMove), Is.True,
                "marked for redeployment next round.");
            Assert.That(aircraft.GetValue().AircraftHeading, Is.Null, "heading cleared so it re-aims when re-placed.");
        }

        [Test]
        public async Task AircraftAdvance_StayingOnTable_DoesNotLeavePlay()
        {
            var ctx = new TriggeredMoveTestContext(_store, new FirstStringRequester());

            DataBinding<UnitData> aircraft = MakeUnit(new Position(10, 5)); // heading lazy-inits toward centre; 30" stays on-table
            aircraft.GetValue().AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));

            var moveCtx = new MovementActionContext(ctx, aircraft);
            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("done");
            await stage.Enter(moveCtx);

            Assert.That(aircraft.GetValue().Tokens.HasToken(TokenType.OffTableFromForcedMove), Is.False,
                "an Aircraft that stays on the table is not removed from play.");
            Assert.That(moveCtx.TryGetPaths(out var paths), Is.True);
            Position dest = paths[0].Positions[0];
            Assert.That(dest.x != 10f || dest.z != 5f, Is.True, "a forced straight-line move was submitted.");
        }

        private DataBinding<UnitData> MakeUnit(Position pos)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), pos, _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Jet", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }

    // Answers any StringSelectionRequest with its first valid option (the Aircraft distance prompt → "30\"").
    internal sealed class FirstStringRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is StringSelectionRequest s)
                return Task.FromResult((TReply)(object)s.ValidOptions[0]);
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
