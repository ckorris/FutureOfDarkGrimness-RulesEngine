using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #216 — a Tactician plan the resolver's re-check rejects must be REPAIRED (re-planned toward
    // the same destination under this request's constraints), not silently swapped for the solo
    // resolver's move. The budget-mismatch arm is pinned in TacticianWalledUnitTests (issue 7,
    // PlannedMove_UnitWithASlowModel_...); these pin the #205 friendly-stacking arm - the scenario
    // #216 was filed about - and the one legitimate degradation (no cached plan at all).
    [TestFixture]
    public class TacticianMovementResolverTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private PlayerID _us;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _us = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task PlanEndingStackedOnAFriendly_IsRepairedTowardTheSameGoal_NotSoloFallback()
        {
            var mover = MakeUnitAt(_us, 4, i => new Position(20f + (i % 2) * 1.1f, 24f + (i / 2) * 1.1f));
            var friendly = MakeUnitAt(_us, 4, i => new Position(30f + (i % 2) * 1.1f, 24f + (i / 2) * 1.1f));
            var living = mover.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();

            // A cached macro-move that parks every model exactly on a friendly base - the #205
            // re-check must reject it, and the repair must then preserve the INTENT (get to ~(30,24))
            // rather than handing the activation to the solo resolver.
            var friendlyModels = friendly.GetValue().ModelBindings.ToList();
            var stackedPlan = living
                .Select((mb, i) => new ModelMoveEntry(mb,
                    new List<Position> { friendlyModels[i].GetValue().Position }))
                .ToList();

            var request = new DefineMovementPathRequest(_us, "Move Unit", mover,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                modelMoveBudgets: new List<ModelMoveBudgetInfo>(), allowCancel: true);

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };
            var friendlies = MovementPlanner.LiveFriendlyFootprints(_tableState, _us,
                mover.GetValue().ID);
            Assert.That(MovementUtilities.ValidatePaths(stackedPlan, budgetFor,
                    new List<EnemyModelFootprint>(), false, false, false,
                    _tableState.Terrain.Objects.ToList(), out _, friendlies, lenientCoherency: true),
                Is.False, "scene check: the stacked plan must fail the #205 re-check, or this pin " +
                "never reaches the repair pass");

            var solo = new RecordingFallbackResolver(mover);
            var resolver = new TacticianMovementResolver(new FixedPlanSource(stackedPlan),
                _tableState, solo);
            CancellableResult<List<ModelMoveEntry>> reply = await resolver.Resolve(request);

            Assert.That(solo.WasCalled, Is.False,
                "a stacked plan must be repaired, not silently degraded to the solo resolver");
            List<ModelMoveEntry> move = ((Selected<List<ModelMoveEntry>>)reply).Value;

            bool valid = MovementUtilities.ValidatePaths(move, budgetFor,
                new List<EnemyModelFootprint>(), false, false, false,
                _tableState.Terrain.Objects.ToList(), out List<ReasonForInvalidMove> reasons,
                friendlies, lenientCoherency: true);
            Assert.That(valid, Is.True,
                $"the repaired move must pass the same re-check that rejected the plan ({string.Join(", ", reasons)})");

            // The repair really moved the endpoints off the friendly bases (0.5" radii both sides:
            // any centre gap under 1" is an overlap the #205 rule forbids).
            foreach (ModelMoveEntry entry in move)
            {
                Position end = entry.Positions[^1];
                float nearest = friendlyModels.Min(fm => Distance(end, fm.GetValue().Position));
                Assert.That(nearest, Is.GreaterThanOrEqualTo(0.99f),
                    $"endpoint ({end.x:F1},{end.z:F1}) still overlaps a friendly base");
            }

            // Intent preserved: the unit closes most of the 10" gap to the planned destination
            // instead of playing whatever the solo resolver would have done.
            var goal = new Position(30.55f, 24.55f);
            float before = Distance(Centroid(living.Select(mb => mb.GetValue().Position)), goal);
            float after = Distance(Centroid(move.Select(e => e.Positions[^1])), goal);
            Assert.That(after, Is.LessThan(before - 5f),
                $"the repair must still head for the plan's destination (closed {before - after:F1}\" of {before:F1}\")");
        }

        [Test]
        public async Task NoCachedPlan_DegradesToSolo()
        {
            // The one legitimate fallback arm: Hold/Pass or no planner claim. Everything else must
            // go through the repair pass first.
            var mover = MakeUnitAt(_us, 2, i => new Position(20f + i * 1.1f, 24f));
            var request = new DefineMovementPathRequest(_us, "Move Unit", mover,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                modelMoveBudgets: new List<ModelMoveBudgetInfo>(), allowCancel: true);

            var solo = new RecordingFallbackResolver(mover);
            var resolver = new TacticianMovementResolver(new FixedPlanSource(null), _tableState, solo);
            await resolver.Resolve(request);

            Assert.That(solo.WasCalled, Is.True,
                "with no cached plan the solo resolver is the intended answer");
        }

        // --- helpers --------------------------------------------------------------------------------

        private sealed class FixedPlanSource : IMovePlanSource
        {
            private readonly List<ModelMoveEntry>? _plan;
            public FixedPlanSource(List<ModelMoveEntry>? plan) => _plan = plan;
            public List<ModelMoveEntry>? TakePlannedMove(DataBinding<UnitData> unit) => _plan;
        }

        private sealed class RecordingFallbackResolver
            : IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>
        {
            private readonly DataBinding<UnitData> _unit;
            public bool WasCalled { get; private set; }

            public RecordingFallbackResolver(DataBinding<UnitData> unit) => _unit = unit;

            public Task<CancellableResult<List<ModelMoveEntry>>> Resolve(DefineMovementPathRequest request)
            {
                WasCalled = true;
                var living = _unit.GetValue().ModelBindings
                    .Where(mb => mb.GetValue().GetIsAlive()).ToList();
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(
                    new Selected<List<ModelMoveEntry>>(MovementPlanner.HoldExactPositions(living)));
            }
        }

        private DataBinding<UnitData> MakeUnitAt(PlayerID owner, int modelCount,
            Func<int, Position> positionFor)
        {
            var weapon = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon }, positionFor(i), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U-{modelCount}", quality: 4,
                defense: 4, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static Position Centroid(IEnumerable<Position> points)
        {
            var list = points.ToList();
            return new Position(list.Average(p => p.x), list.Average(p => p.z));
        }

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
