using FDG.Ai.Tactician;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A3b — grid pathfinding on authored terrain: the capability the old angular skirting could
    // not deliver (threading a narrow corridor), plus the engine-valid composition through the ladder.
    [TestFixture]
    public class GridPathfinderTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
        }

        [Test]
        public void FindPath_NoTerrain_IsTheStraightLine()
        {
            var terrain = new List<ITerrain>();
            TerrainGrid grid = TerrainGrid.Build(terrain, baseRadiusInches: 0.5f);

            List<Position>? path = GridPathfinder.FindPath(grid, terrain,
                new Position(5f, 24f), new Position(40f, 24f), 0.5f);

            Assert.That(path, Is.Not.Null);
            Assert.That(path!.Count, Is.EqualTo(2), "a clear lane needs no intermediate waypoints");
        }

        [Test]
        public void FindPath_WallBetween_RoutesAroundWithoutTouchingIt()
        {
            // A wall across x=20..22 spanning most of the table height; gap at the top.
            ITerrain wall = MakeTerrain(ETerrainType.Impassible, left: 20f, right: 22f, bottom: 0f, top: 40f);
            var terrain = new List<ITerrain> { wall };
            TerrainGrid grid = TerrainGrid.Build(terrain, 0.5f);

            List<Position>? path = GridPathfinder.FindPath(grid, terrain,
                new Position(10f, 24f), new Position(35f, 24f), 0.5f);

            Assert.That(path, Is.Not.Null, "a route around the wall exists");
            AssertNoLegTouchesImpassible(path!, terrain, 0.5f);
        }

        [Test]
        public void FindPath_NarrowCorridor_ThreadsTheGap()
        {
            // Two walls leaving a 4" corridor at z 22..26 — the case plan D5 names: angular skirting
            // fails by construction; goal-directed search must thread it.
            var terrain = new List<ITerrain>
            {
                MakeTerrain(ETerrainType.Impassible, left: 20f, right: 24f, bottom: 0f, top: 22f),
                MakeTerrain(ETerrainType.Impassible, left: 20f, right: 24f, bottom: 26f, top: 48f),
            };
            TerrainGrid grid = TerrainGrid.Build(terrain, 0.5f);

            List<Position>? path = GridPathfinder.FindPath(grid, terrain,
                new Position(10f, 10f), new Position(40f, 40f), 0.5f);

            Assert.That(path, Is.Not.Null, "the corridor is passable");
            AssertNoLegTouchesImpassible(path!, terrain, 0.5f);
            // The route must actually pass through the corridor band while crossing the wall's x-range.
            bool threadsCorridor = Legs(path!).Any(leg =>
                (leg.From.x <= 24f && leg.To.x >= 20f || leg.To.x <= 24f && leg.From.x >= 20f)
                && MathF.Min(leg.From.z, leg.To.z) >= 21f && MathF.Max(leg.From.z, leg.To.z) <= 27f);
            Assert.That(threadsCorridor, Is.True, "the path must go THROUGH the corridor, not around the table edge");
        }

        [Test]
        public void FindPath_SealedGoal_ReturnsNull()
        {
            var terrain = new List<ITerrain>
            {
                MakeTerrain(ETerrainType.Impassible, left: 30f, right: 40f, bottom: 18f, top: 20f),
                MakeTerrain(ETerrainType.Impassible, left: 30f, right: 40f, bottom: 28f, top: 30f),
                MakeTerrain(ETerrainType.Impassible, left: 30f, right: 32f, bottom: 18f, top: 30f),
                MakeTerrain(ETerrainType.Impassible, left: 38f, right: 40f, bottom: 18f, top: 30f),
            };
            TerrainGrid grid = TerrainGrid.Build(terrain, 0.5f);

            List<Position>? path = GridPathfinder.FindPath(grid, terrain,
                new Position(10f, 24f), new Position(35f, 24f), 0.5f);

            Assert.That(path, Is.Null, "a fully sealed goal has no route - candidate is infeasible, not wrong");
        }

        [Test]
        public void AdvanceAlongPath_StopsMidLegAtBudget_AndReportsPassedWaypoints()
        {
            var path = new List<Position>
                { new Position(0f, 0f), new Position(10f, 0f), new Position(10f, 10f) };

            (Position end, List<Position> passed, bool difficult) =
                GridPathfinder.AdvanceAlongPath(path, budgetInches: 14f, new List<ITerrain>(), 0.5f);

            Assert.That(passed, Has.Count.EqualTo(1), "the first corner was traversed");
            Assert.That(end.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(end.z, Is.EqualTo(4f).Within(0.001f), "10\" along leg one + 4\" along leg two");
            Assert.That(difficult, Is.False);
        }

        [Test]
        public void PlanMoveToward_DifficultRoute_CapsTheMoveAtSix()
        {
            MakeTerrainInStore(ETerrainType.Difficult, left: 12f, right: 30f, bottom: 20f, top: 28f);
            var unit = MakeUnit(3, atX: 10f, atZ: 24f);
            var living = unit.GetValue().ModelBindings.ToList();

            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, living, _tableState,
                new Position(40f, 24f), moveBudgetInches: 12f, maxDistanceInches: 12f,
                _ => new ModelMoveBudget(12f, 12f),
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false);

            foreach (ModelMoveEntry entry in move)
            {
                float travelled = PathLength(entry);
                Assert.That(travelled, Is.LessThanOrEqualTo(GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES),
                    "crossing difficult ground caps the whole move at 6\"");
            }
        }

        [Test]
        public void PlanMoveToward_CorridorMap_ProducesEngineValidProgress()
        {
            MakeTerrainInStore(ETerrainType.Impassible, left: 20f, right: 24f, bottom: 0f, top: 22f);
            MakeTerrainInStore(ETerrainType.Impassible, left: 20f, right: 24f, bottom: 26f, top: 48f);
            var unit = MakeUnit(3, atX: 14f, atZ: 24f);
            var living = unit.GetValue().ModelBindings.ToList();
            var goal = new Position(40f, 24f);

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = _ => new ModelMoveBudget(12f, 12f);
            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, living, _tableState,
                goal, moveBudgetInches: 12f, maxDistanceInches: 12f, budgetFor,
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false);

            // The ladder's contract: whatever comes back passes the stage's own validator.
            bool valid = MovementUtilities.ValidatePaths(move, budgetFor,
                new List<EnemyModelFootprint>(), false, false, false,
                _tableState.Terrain.Objects.ToList(), out List<ReasonForInvalidMove> reasons);
            Assert.That(valid, Is.True,
                $"planner must never emit an engine-invalid move ({string.Join(", ", reasons)})");

            // And it must make real progress toward the goal (the old skirting froze here).
            float before = Distance(Centroid(living), goal);
            foreach (ModelMoveEntry entry in move)
                entry.Model.GetValue().SetPosition(entry.Positions[^1]);
            float after = Distance(Centroid(living), goal);
            Assert.That(after, Is.LessThan(before - 4f),
                "the unit must advance meaningfully toward the goal through the corridor");
        }

        // --- fixtures ---------------------------------------------------------------------------

        private static ITerrain MakeTerrain(ETerrainType type, float left, float right, float bottom, float top)
            => new TerrainData(type, new RectangularZone(left, right, bottom, top));

        private void MakeTerrainInStore(ETerrainType type, float left, float right, float bottom, float top)
            => _store.Create(new TerrainData(type, new RectangularZone(left, right, bottom, top)));

        private DataBinding<UnitData> MakeUnit(int modelCount, float atX, float atZ)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static IEnumerable<(Position From, Position To)> Legs(List<Position> path)
        {
            for (int i = 1; i < path.Count; i++)
                yield return (path[i - 1], path[i]);
        }

        private static void AssertNoLegTouchesImpassible(List<Position> path, List<ITerrain> terrain, float radius)
        {
            foreach ((Position from, Position to) in Legs(path))
            {
                bool touches = terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Impassible)
                    && t.Shape.DoesPathIntersectZone(new Float2(from.x, from.z), new Float2(to.x, to.z), radius));
                Assert.That(touches, Is.False, $"leg ({from.x},{from.z})->({to.x},{to.z}) clips impassible terrain");
            }
        }

        private static float PathLength(ModelMoveEntry entry)
        {
            float total = 0f;
            Position current = entry.Model.GetValue().Position;
            foreach (Position next in entry.Positions)
            {
                total += Distance(current, next);
                current = next;
            }
            return total;
        }

        private static Position Centroid(List<DataBinding<ModelData>> models) => new Position(
            models.Average(mb => mb.GetValue().Position.x),
            models.Average(mb => mb.GetValue().Position.z));

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
