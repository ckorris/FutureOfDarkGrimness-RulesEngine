using FDG.Ai.Resolvers;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #264 — a Tactician unit deployed behind a large impassible piece rushes sideways/backwards
    // (or freezes) instead of walking around it. These tests pin the DESIRED behavior for each
    // suspected mechanism and are RED BY DESIGN until the #264 fixes land - the pass/fail metric
    // agreed before any fix is written. Run the rest of the suite green with:
    //   dotnet test --filter TestCategory!=Pending264
    // Issue numbers reference WorkItems/264-tactician-walled-unit-lateral-retreat.md.
    [TestFixture, Category("Pending264")]
    public class TacticianWalledUnitTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us;
        private PlayerID _them;
        private List<string> _decisions = null!;

        private static readonly string[] MoveOrPass =
        {
            ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.CHARGE_CHOICE_NAME,
            ChooseActionStage.PASS_CHOICE_NAME, // no Shoot: nothing is in range, the engine gates it
        };

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _decisions = new List<string>();
        }

        // --- The Knight-Brothers shape (issues 1 + 2): 11 rifles behind a 20"-wide impassible
        // wall, one objective dead ahead beyond it, an enemy gunline farther out. Every substantive
        // score term is ~0 for the correct detour (the Euclidean gradient pays nothing for rounding
        // a wall - the first leg can even move AWAY from the marker in straight-line terms), while
        // trivially-reachable retreat candidates collect the flat MoveReachableBonus and dodge the
        // retaliation that closing candidates pay. The argmax retreats or freezes.

        private DataBinding<UnitData> MakeWalledScene()
        {
            // Wall x 14..34, z 10..12; unit packed 4-wide at (~24, ~7); objective at (24,24)
            // behind the wall; 5 enemy rifles at (~24.5, ~35) - out of our 24" reach from here,
            // but their reach covers any forward endpoint (closing pays retaliation, staying
            // does not).
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(14f, 34f, 10f, 12f)));
            _store.Create(new ObjectiveData(new Position(24f, 24f), _store));
            var unit = MakeUnitAt(_us, 11, Rifle(),
                i => new Position(22.4f + (i % 4) * 1.1f, 6f + (i / 4) * 1.1f));
            MakeUnitAt(_them, 5, Rifle(),
                i => new Position(24f + (i % 2) * 1.1f, 34f + (i / 2) * 1.1f));
            return unit;
        }

        [Test]
        public void WalledUnit_ArgmaxMove_MakesRealProgressTowardTheObjective()
        {
            // Issue 1 (headline): the chosen activation must spend the move getting AROUND the
            // wall - measured along the true (pathfound) route to the marker, because a correct
            // detour's first leg may legitimately grow the straight-line distance.
            DataBinding<UnitData> unit = MakeWalledScene();
            var planner = new TacticianPlanner(_tableState, _evaluator, _decisions.Add);
            planner.BeginActivation(unit);

            string? action = planner.ChooseAction(MoveOrPass);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "an uncontested marker two moves out must start the walk, not pass/freeze\n" + DecisionTable());
            List<ModelMoveEntry>? move = planner.TakePlannedMove(unit);
            Assert.That(move, Is.Not.Null, "the movement action must carry a planned move\n" + DecisionTable());

            var objective = new Position(24f, 24f);
            Position start = Centroid(unit);
            Position end = EndCentroid(move!, unit);
            float closed = PathDistance(start, objective) - PathDistance(end, objective);
            Assert.That(closed, Is.GreaterThanOrEqualTo(1f),
                $"the move must close real route distance to the marker (start=({start.x:F1},{start.z:F1}) " +
                $"end=({end.x:F1},{end.z:F1}))\n" + DecisionTable());
        }

        [Test]
        public void WalledUnit_RushingTheObjective_OutscoresFullDistanceRetreat()
        {
            // Issues 1 + 2, the score-ordering pin: the detour toward the only marker must outscore
            // walking backwards. Today the detour's approach term is ~0 (Euclidean gradient), it
            // pays retaliation for closing, and the retreat is safe + Reachable (flat bonus) - the
            // ordering inverts.
            DataBinding<UnitData> unit = MakeWalledScene();
            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(unit);
            List<MacroAction> candidates = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);

            MacroAction rush = candidates.First(c => c.Intent == EMacroIntent.RushObjective);
            MacroAction fallBack = candidates.First(c => c.Intent == EMacroIntent.FallBack);
            float rushScore = planner.Score(rush);
            float fallBackScore = planner.Score(fallBack);

            Assert.That(rushScore, Is.GreaterThan(fallBackScore),
                $"rounding the wall toward the only marker (score {rushScore:F4}, end " +
                $"({rush.ProjectedCentroid.x:F1},{rush.ProjectedCentroid.z:F1})) must beat a full-distance " +
                $"retreat (score {fallBackScore:F4}, end ({fallBack.ProjectedCentroid.x:F1},{fallBack.ProjectedCentroid.z:F1}))");
        }

        [Test]
        public void WalledUnit_ThreeActivations_EscapeTheWallPocket()
        {
            // Issue 8b (+ the integrated 1/2/3 behavior): re-argmaxing from scratch each activation
            // must still produce cumulative progress - three rushes cover the ~29" route, so the
            // unit must ARRIVE, not oscillate lateral/backward in the pocket.
            DataBinding<UnitData> unit = MakeWalledScene();
            var planner = new TacticianPlanner(_tableState, _evaluator, _decisions.Add);
            var objective = new Position(24f, 24f);

            for (int activation = 0; activation < 3; activation++)
            {
                planner.BeginActivation(unit);
                planner.ChooseAction(MoveOrPass);
                List<ModelMoveEntry>? move = planner.TakePlannedMove(unit);
                if (move == null) continue; // Hold/Pass: no position change
                foreach (ModelMoveEntry entry in move)
                    if (entry.Positions.Count > 0)
                        entry.Model.GetValue().SetPosition(entry.Positions[^1]);
            }

            Position final = Centroid(unit);
            Assert.That(Distance(final, objective),
                Is.LessThanOrEqualTo(TacticalAnalysis.ObjectiveSeizureRadiusInches + 1.5f),
                $"three rushes must round a 20\" wall and take the marker; unit ended at " +
                $"({final.x:F1},{final.z:F1})\n" + DecisionTable());
        }

        // --- Issue 3: FindPath returns null when the GOAL CELL is inside the grid's inflated
        // blocked region - even for a goal a base can legally stand on (cell-center conservatism)
        // or approach within seizure radius. PlanMoveToward then falls back to the straight line
        // THROUGH the wall; the snake follows the same line, so the #256 S4 rescue is dead exactly
        // when pathfinding fails, and the ladder halves into the wall face - forever.

        [Test]
        public void PlanMoveToward_GoalCellInsideWallInflation_StillRoundsTheWall()
        {
            // Goal (24, 12.9) sits 0.7" past the wall's far face - legally standable (base edge
            // 12.4 > wall top 12.2) but its 1" grid cell center (24.5, 12.5) is inside the
            // 0.5"-inflated blocked region, so FindPath returns null. A real route around the
            // east corner exists and three 12" moves cover it; today every activation crawls
            // half the remaining gap into the near wall face and the unit never arrives.
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(14f, 34f, 10f, 12.2f)));
            var unit = MakeUnitAt(_us, 6, Rifle(),
                i => new Position(23f + (i % 3) * 1.1f, 4f + (i / 3) * 1.1f));
            var living = unit.GetValue().ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            var goal = new Position(24f, 12.9f);

            for (int activation = 0; activation < 3; activation++)
            {
                List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, living, _tableState,
                    goal, moveBudgetInches: 12f, maxDistanceInches: 12f,
                    budgetFor: _ => new ModelMoveBudget(12f, 12f),
                    canMoveThroughEnemies: false, ignoresDifficultTerrain: false,
                    ignoresImpassibleTerrain: false);
                foreach (ModelMoveEntry entry in move)
                    if (entry.Positions.Count > 0)
                        entry.Model.GetValue().SetPosition(entry.Positions[^1]);
            }

            Position final = Centroid(unit);
            Assert.That(Distance(final, goal), Is.LessThanOrEqualTo(2.5f),
                $"a null pathfind must degrade to the nearest REACHABLE approach, not a straight " +
                $"line into the wall face; unit ended at ({final.x:F1},{final.z:F1})");
        }

        // --- Issue 4: BuildPathCandidate routes EVERY model through the path's shared interior
        // waypoints. For a wide formation at a wall corner the flank models burn their whole
        // budget detouring to waypoint 1, the measure-and-correct loop shrinks the arc toward
        // zero, and the whole unit near-stays.

        [Test]
        public void PlanMoveToward_WideFormationAtWallCorner_KeepsMostOfItsBudget()
        {
            // 11 models in a single 11"-wide rank under the wall, east end at the corner; the
            // route's first bend (past the corner) is INSIDE the 12" budget, so BuildPathCandidate
            // keeps it as a shared waypoint and the west-flank models burn their whole budget
            // detouring to it - the measure-and-correct loop then shrinks the arc to ~1" for
            // everyone. (Centered under the wall the bend falls outside the budget and the loop
            // degrades gracefully - the burn is corner-hugging-specific, confirmed by probe.)
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(14f, 34f, 10f, 12f)));
            var unit = MakeUnitAt(_us, 11, Rifle(), i => new Position(24f + i * 1.1f, 8f));
            var living = unit.GetValue().ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            (float cx, float cz) = (living.Average(mb => mb.GetValue().Position.x),
                living.Average(mb => mb.GetValue().Position.z));

            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, living, _tableState,
                goal: new Position(29.5f, 24f), moveBudgetInches: 12f, maxDistanceInches: 12f,
                budgetFor: _ => new ModelMoveBudget(12f, 12f),
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false,
                ignoresImpassibleTerrain: false);

            Assert.That(NetCentroidMove(move, cx, cz), Is.GreaterThanOrEqualTo(2.5f),
                "a wide formation must not burn its budget funneling through shared waypoints");
        }

        // --- Issue 5: the #256 rescue gates are all-or-nothing. S2 re-aim requires errors.All
        // (EndedOnFriendlyUnit); S4 snake requires errors.All(MovingThroughImpassibleTerrain).
        // A candidate carrying BOTH error types (round-1 density: wall + adjacent friendly)
        // disables both rescues and the ladder halves to a sub-inch shuffle.

        [Test]
        public void PlanMoveToward_WallAndFriendlyMixedErrors_StillThreadsTheCorridor()
        {
            // The #256 corridor scene (2" gap between walls, 6-model unit south of it) plus one
            // friendly parked on the corridor centerline at the mouth - snake destinations land on
            // the friendly, grid packs clip the walls, and mid-ladder candidates carry both error
            // types at once.
            var unit = MakeUnitAt(_us, 6, Rifle(), i => new Position(20f + (i % 3), 20f + (i / 3)));
            var living = unit.GetValue().ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            float cx = living.Average(mb => mb.GetValue().Position.x);   // 21
            float cz = living.Average(mb => mb.GetValue().Position.z);   // 20.5
            _store.Create(new TerrainData(ETerrainType.Impassible,
                new RectangularZone(cx - 15f, cx - 1f, cz + 3f, cz + 9f)));
            _store.Create(new TerrainData(ETerrainType.Impassible,
                new RectangularZone(cx + 1f, cx + 15f, cz + 3f, cz + 9f)));
            var friendly = MakeUnitAt(_us, 1, Rifle(), _ => new Position(cx, cz + 2f));

            // Geometry guard against a vacuous pass: a mid-ladder centered candidate really does
            // carry BOTH error types (walls on the flanks, the friendly under a center slot).
            List<ModelMoveEntry> centered = MovementPlanner.BuildCandidate(unit, living, cx, cz,
                ndx: 0f, ndz: 1f, step: 3f, maxDistanceInches: 12f);
            MovementUtilities.ValidatePaths(centered, _ => new ModelMoveBudget(12f, 12f),
                new List<EnemyModelFootprint>(), false, false, false,
                _tableState.Terrain.Objects.ToList(), out List<ReasonForInvalidMove> errors,
                FriendlyFootprints(friendly), lenientCoherency: true);
            Assert.That(errors.Select(e => e.ErrorReasonType).Distinct(),
                Is.SupersetOf(new[]
                {
                    EErrorReasonType.MovingThroughImpassibleTerrain,
                    EErrorReasonType.EndedOnFriendlyUnit,
                }),
                "geometry check: the centered candidate must carry BOTH error types " +
                $"(got: {string.Join(", ", errors.Select(e => e.ErrorReasonType).Distinct())})");

            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, living, _tableState,
                goal: new Position(cx, 44f), moveBudgetInches: 12f, maxDistanceInches: 12f,
                budgetFor: _ => new ModelMoveBudget(12f, 12f),
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false,
                ignoresImpassibleTerrain: false);

            Assert.That(NetCentroidMove(move, cx, cz), Is.GreaterThanOrEqualTo(2f),
                "mixed wall + friendly faults must still be rescued, not halved to a shuffle");
        }

        // --- Issue 6: the solo resolver (every Tactician fallback lands here) skirts blocked
        // lanes at fixed angles up to +/-100 degrees - PAST perpendicular - at the full rush
        // budget. A melee unit walled off from its target then lurches 12" sideways-to-backwards.

        [Test]
        public async Task SoloResolver_OnlyWideSkirtAnglesClear_DoesNotRushPastPerpendicular()
        {
            // A tall wall east of the mover blocks every direction within ~80 degrees of the
            // enemy lane; only the +/-100 degree skirts are clear. Whatever the resolver does, the
            // move must keep a positive component TOWARD the enemy (stand, shorten, or route -
            // never a full-budget move pointing away).
            Position start = new Position(24f, 20f);
            var mover = MakeUnitAt(_us, 1, Blade(), _ => start);
            MakeUnitAt(_them, 1, Blade(), _ => new Position(40f, 20f));
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(26f, 38f, 4f, 36f)));

            var resolver = new AiDefineMovementResolver(_tableState, _us);
            var request = new DefineMovementPathRequest(_us, "Move Unit", mover,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f);

            CancellableResult<List<ModelMoveEntry>> reply = await resolver.Resolve(request);
            List<ModelMoveEntry> move = ((Selected<List<ModelMoveEntry>>)reply).Value;

            Position end = move[0].Positions.Count > 0 ? move[0].Positions[^1] : start;
            float towardEnemy = end.x - start.x; // the enemy is due east
            float moved = Distance(start, end);
            Assert.That(towardEnemy, Is.GreaterThan(moved > 1f ? 0f : -0.01f),
                $"a substantial move must keep a positive component toward the enemy; " +
                $"moved {moved:F1}\" to ({end.x:F1},{end.z:F1})");
        }

        // --- Issue 7 (latent): MacroActionGenerator.Plan validates with a FLAT unit budget while
        // TacticianMovementResolver re-checks the cached plan against the request's PER-MODEL
        // budgets. A joined Slow hero makes every planned move fail the re-check, silently
        // degrading the unit to the solo resolver for the whole game.

        [Test]
        public async Task PlannedMove_UnitWithASlowModel_IsSubmittedNotSoloFallback()
        {
            _store.Create(new ObjectiveData(new Position(30f, 25f), _store));
            var unit = MakeUnitAt(_us, 6, Rifle(), i => new Position(20f + (i % 2) * 1.1f, 24f + (i / 2) * 1.1f));
            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(unit);
            string? action = planner.ChooseAction(MoveOrPass);
            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "scene check: the marker one rush out must be taken");

            // The engine's request carries the Slow hero's own budget (#093): rush 8 instead of 12.
            ModelID slowModel = unit.GetValue().ModelBindings[0].GetValue().ID;
            var request = new DefineMovementPathRequest(_us, "Move Unit", unit,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                modelMoveBudgets: new List<ModelMoveBudgetInfo>
                    { new ModelMoveBudgetInfo(slowModel, 4f, 8f, 8f) },
                allowCancel: true);

            var solo = new RecordingFallbackResolver(unit);
            var resolver = new TacticianMovementResolver(planner, _tableState, solo);
            CancellableResult<List<ModelMoveEntry>> reply = await resolver.Resolve(request);

            Assert.That(solo.WasCalled, Is.False,
                "the planner's move must be planned within per-model budgets and submitted - " +
                "not silently degraded to the solo resolver by the re-check");
            List<ModelMoveEntry> move = ((Selected<List<ModelMoveEntry>>)reply).Value;
            bool valid = MovementUtilities.ValidatePaths(move,
                entry => { var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                           return new ModelMoveBudget(rush, maxDist); },
                new List<EnemyModelFootprint>(), false, false, false,
                _tableState.Terrain.Objects.ToList(), out List<ReasonForInvalidMove> reasons,
                null, lenientCoherency: true);
            Assert.That(valid, Is.True,
                $"the submitted move must honour the Slow model's budget ({string.Join(", ", reasons)})");
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

        // --- Issue 8a: deployment aims at objective lanes but only avoids OVERLAPPING terrain,
        // so a big unit parks squarely behind a wall spanning its lane - the self-trap that set up
        // Chris's game. The chosen center's route to its aim must not carry a huge detour penalty.

        [Test]
        public async Task Deployment_ObjectiveLaneWalledOff_DoesNotParkInThePocket()
        {
            _store.Create(new ObjectiveData(new Position(24f, 24f), _store));
            // Wall just OUTSIDE the deployment zone (no overlap), spanning the objective lane.
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(14f, 34f, 12.5f, 14.5f)));
            var zone = new RectangularZone(left: 0f, right: 48f, bottom: 0f, top: 12f);
            var resolver = new TacticianPlaceObjectsResolver<ModelData>(_tableState);
            var models = MakeUnitAt(_us, 11, Blade(), _ => new Position(0f, 0f))
                .GetValue().ModelBindings.ToList();

            var reply = await resolver.Resolve(new PlaceObjectsRequest<ModelData>(
                _us, TacticianPlaceObjectsResolver<ModelData>.DeploymentTaskName, zone, models));
            var placed = ((Selected<List<PlacedObjectEntry<ModelData>>>)reply).Value;

            var centroid = new Position(placed.Average(p => p.Position.x), placed.Average(p => p.Position.z));
            var objective = new Position(24f, 24f);
            float detourPenalty = PathDistance(centroid, objective) - Distance(centroid, objective);
            Assert.That(detourPenalty, Is.LessThanOrEqualTo(4f),
                $"deployed at ({centroid.x:F1},{centroid.z:F1}): the route to the objective carries a " +
                $"{detourPenalty:F1}\" detour - the unit deployed into the wall pocket");
        }

        // --- helpers --------------------------------------------------------------------------------

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
        private static Weapon Blade() => new Weapon("Blade", rangeInches: 0f, attacks: 2, armorPenetration: 0);

        private DataBinding<UnitData> MakeUnitAt(PlayerID owner, int modelCount, Weapon weapon,
            Func<int, Position> positionFor)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon }, positionFor(i), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{owner.GetHashCode() % 100}-{modelCount}", quality: 4,
                defense: 4, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private string DecisionTable() =>
            _decisions.Count == 0 ? "(no decision log)" : string.Join("\n", _decisions);

        private static List<EnemyModelFootprint> FriendlyFootprints(DataBinding<UnitData> unit) =>
            unit.GetValue().Models.Where(m => m.GetIsAlive())
                .Select(m => new EnemyModelFootprint(m.Position, m.BaseRadiusInches, 0, false,
                    ((ModelData)m).BaseShape, ((ModelData)m).Facing))
                .ToList();

        /// <summary>Route distance around impassible terrain (base-radius-inflated); the honest
        /// progress metric behind a wall, where a correct detour transiently grows the Euclidean
        /// distance. Infinity when no route exists.</summary>
        private float PathDistance(Position from, Position to, float baseRadius = 0.5f)
        {
            var terrain = _tableState.Terrain.Objects.ToList();
            TerrainGrid grid = TerrainGrid.Build(terrain, baseRadius);
            List<Position>? path = GridPathfinder.FindPath(grid, terrain, from, to, baseRadius);
            if (path == null) return float.PositiveInfinity;
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Distance(path[i - 1], path[i]);
            return total;
        }

        private static Position Centroid(DataBinding<UnitData> unit)
        {
            var alive = unit.GetValue().Models.Where(m => m.GetIsAlive()).ToList();
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        private static Position EndCentroid(List<ModelMoveEntry> move, DataBinding<UnitData> unit)
        {
            var ends = move.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0) return Centroid(unit);
            return new Position(ends.Average(p => p.x), ends.Average(p => p.z));
        }

        private static float NetCentroidMove(List<ModelMoveEntry> move, float cx, float cz)
        {
            var ends = move.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0) return 0f;
            float ex = ends.Average(p => p.x), ez = ends.Average(p => p.z);
            return MathF.Sqrt((ex - cx) * (ex - cx) + (ez - cz) * (ez - cz));
        }

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
