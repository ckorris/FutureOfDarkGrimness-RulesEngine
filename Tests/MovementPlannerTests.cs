using FDG.Ai.Tactician;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A3a — the genuinely NEW planner piece: the Line formation (M8 Block's barrier shape).
    // The extracted Grid machinery is covered by AiDefineMovementResolverTests + CohesiveFormationTests
    // (the pin), plus the benchmark hash comparison recorded in the #191 ledger.
    [TestFixture]
    public class MovementPlannerTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void PackLine_SmallUnit_FormsOneCohesiveRankAlongAxis()
        {
            var models = MakeModels(5, baseRadius: 0.5f);

            List<ModelMoveEntry> line = MovementPlanner.PackLine(models, 20f, 20f, axisDx: 1f, axisDz: 0f);
            ApplyMoves(line);

            Assert.That(CohesiveFormation.IsCohesive(models), Is.True, "a packed line must satisfy cohesion");
            // Along the X axis the rank spreads out; across it the models stay in one thin band.
            float spanX = Span(models, m => m.Position.x);
            float spanZ = Span(models, m => m.Position.z);
            Assert.That(spanX, Is.GreaterThan(3f), "the line must actually spread along the axis");
            Assert.That(spanZ, Is.LessThan(0.5f), "a 5-model line fits one rank - no perpendicular spread");
        }

        [Test]
        public void PackLine_LongUnit_WrapsToStayWithinCoherency()
        {
            // 12 small bases in one rank would span ~13" and break the 9" all-models rule; the
            // planner must wrap into ranks instead.
            var models = MakeModels(12, baseRadius: 0.5f);

            List<ModelMoveEntry> line = MovementPlanner.PackLine(models, 20f, 20f, axisDx: 1f, axisDz: 0f);
            ApplyMoves(line);

            Assert.That(CohesiveFormation.IsCohesive(models), Is.True,
                "a long line must wrap ranks rather than break the 9\" coherency rule");
        }

        [Test]
        public void PackLine_DiagonalAxis_SpreadsAlongThatAxis()
        {
            var models = MakeModels(4, baseRadius: 0.5f);
            float inv = 1f / MathF.Sqrt(2f);

            List<ModelMoveEntry> line = MovementPlanner.PackLine(models, 10f, 10f, axisDx: inv, axisDz: inv);
            ApplyMoves(line);

            // Projected onto the axis the models spread; onto the perpendicular they don't.
            float axisSpan = Span(models, m => (m.Position.x + m.Position.z) * inv);
            float perpSpan = Span(models, m => (m.Position.x - m.Position.z) * inv);
            Assert.That(axisSpan, Is.GreaterThan(2.5f));
            Assert.That(perpSpan, Is.LessThan(0.5f));
        }

        // --- #256: measure-and-correct replaced the worst-case ClampRepackStep pre-clamp, which
        // subtracted spread + grid radius from the budget and left big combined units unable to
        // advance at all (an 11-model unit's 4" advance clamped to 0.00 in an open field - the
        // WayTooManyInBack save). These pin that a big unit spends most of its budget while every
        // model stays within it.

        [Test]
        public void BuildCandidate_BigUnit_AdvancesMostOfItsBudget()
        {
            var models = MakeModels(11, baseRadius: 0.5f);
            DataBinding<UnitData> unit = MakeUnit(models);
            (float cx, float cz) = Centroid(models);

            List<ModelMoveEntry> move = MovementPlanner.BuildCandidate(unit, models,
                cx, cz, ndx: 1f, ndz: 0f, step: 4f, maxDistanceInches: 4f);

            Assert.That(MaxPathLength(move), Is.LessThanOrEqualTo(4.001f),
                "every model must stay within the move budget");
            Assert.That(NetCentroidMove(move, cx, cz), Is.GreaterThan(3f),
                "a tight 11-model unit must spend most of a 4\" budget, not stand still");
            ApplyMoves(move);
            Assert.That(CohesiveFormation.IsCohesive(models), Is.True);
        }

        [Test]
        public void BuildPathCandidate_BigUnit_AdvancesMostOfItsBudget()
        {
            var models = MakeModels(11, baseRadius: 0.5f);
            DataBinding<UnitData> unit = MakeUnit(models);
            (float cx, float cz) = Centroid(models);
            var path = new List<Position> { new Position(cx, cz), new Position(cx + 30f, cz) };

            List<ModelMoveEntry> move = MovementPlanner.BuildPathCandidate(unit, models, path,
                arcLengthInches: 4f, terrain: new List<ITerrain>(), baseRadiusInches: 0.5f,
                maxDistanceInches: 4f);

            Assert.That(MaxPathLength(move), Is.LessThanOrEqualTo(4.001f));
            Assert.That(NetCentroidMove(move, cx, cz), Is.GreaterThan(3f));
        }

        [Test]
        public void PlanMoveToward_BigUnitOpenField_AdvancesMostOfItsBudget()
        {
            var models = MakeModels(11, baseRadius: 0.5f);
            DataBinding<UnitData> unit = MakeUnit(models);
            (float cx, float cz) = Centroid(models);
            var tableState = new TableState(_store);

            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, models, tableState,
                goal: new Position(cx + 30f, cz), moveBudgetInches: 4f, maxDistanceInches: 4f,
                budgetFor: _ => new ModelMoveBudget(4f, 4f),
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false,
                ignoresImpassibleTerrain: false);

            Assert.That(NetCentroidMove(move, cx, cz), Is.GreaterThan(3f),
                "the ladder-validated open-field advance must survive, not collapse to a stay");
        }

        // --- #256 S2: re-aim instead of halve on friendly stacking. When a centered re-pack's ONLY
        // fault is that it ends on a friendly, ValidateWithBackoff side-steps the pack anchor a few base
        // widths before conceding distance to the halving ladder - so a blocked advance keeps most of its
        // budget instead of crawling toward zero (the WayTooManyInBack corner-pocket case).

        [Test]
        public void ValidateWithBackoff_FriendlyBlocksCenteredAdvance_SideStepsAndKeepsAdvance()
        {
            var models = MakeModels(6, baseRadius: 0.5f);
            DataBinding<UnitData> unit = MakeUnit(models);
            (float cx, float cz) = Centroid(models);
            const float ndx = 1f, ndz = 0f, step = 4f, maxDist = 4f;
            var noEnemies = new List<EnemyModelFootprint>();
            var noTerrain = new List<ITerrain>();

            // Drop a friendly exactly on one of the centered advance's model slots, so a straight move
            // ends stacked - and confirm that is the SOLE fault (what S2 keys on).
            List<ModelMoveEntry> centered = MovementPlanner.BuildCandidate(unit, models, cx, cz, ndx, ndz, step, maxDist);
            Position blocked = centered.First(e => e.Positions.Count > 0).Positions[^1];
            ModelData sample = models[0].GetValue();
            var friendlies = new List<EnemyModelFootprint>
                { new EnemyModelFootprint(blocked, sample.BaseRadiusInches, 0, false, sample.BaseShape, sample.Facing) };

            bool centeredValid = MovementUtilities.ValidatePaths(centered, _ => new ModelMoveBudget(maxDist, maxDist),
                noEnemies, false, false, false, noTerrain, out List<ReasonForInvalidMove> errs, friendlies, lenientCoherency: true);
            Assert.That(centeredValid, Is.False, "the centered advance must be blocked by the friendly");
            Assert.That(errs.All(e => e.ErrorReasonType == EErrorReasonType.EndedOnFriendlyUnit), Is.True,
                "the only fault must be ending on the friendly - the case a side-step can clear");

            List<ModelMoveEntry> move = MovementPlanner.ValidateWithBackoff(
                s => MovementPlanner.BuildCandidate(unit, models, cx, cz, ndx, ndz, s, maxDist),
                step, unit, models, _ => new ModelMoveBudget(maxDist, maxDist),
                noEnemies, false, false, false, noTerrain, friendlies,
                (s, lat) => MovementPlanner.BuildCandidate(unit, models, cx, cz, ndx, ndz, s, maxDist, lateralOffsetInches: lat));

            bool moveValid = MovementUtilities.ValidatePaths(move, _ => new ModelMoveBudget(maxDist, maxDist),
                noEnemies, false, false, false, noTerrain, out _, friendlies, lenientCoherency: true);
            Assert.That(moveValid, Is.True, "the re-aimed move must pass the same validator the stage uses");
            Assert.That(MaxPathLength(move), Is.LessThanOrEqualTo(4.001f), "still within the per-model budget");

            var ends = move.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            float ex = ends.Average(p => p.x), ez = ends.Average(p => p.z);
            Assert.That(NetCentroidMove(move, cx, cz), Is.GreaterThan(2.5f),
                "the side-step must preserve most of the advance, not halve toward a stall");
            Assert.That(ex - cx, Is.GreaterThan(2f), "most of the advance is still forward");
            Assert.That(Math.Abs(ez - cz), Is.GreaterThan(0.4f), "the pack really side-stepped off the blocked axis");
        }

        [Test]
        public void PlanMoveToward_FriendlyOnArrivalSpot_SideStepsAndKeepsAdvance()
        {
            // The Tactician's objective advances route through PlanMoveToward/BuildPathCandidate (even in
            // an open field - the trivial 2-point path), so the re-aim must work there too, not just for
            // the straight candidate (the WayTooManyInBack "Warriors toward (7,30)" halving row).
            var models = MakeModels(6, baseRadius: 0.5f);
            DataBinding<UnitData> unit = MakeUnit(models);
            (float cx, float cz) = Centroid(models);
            var tableState = new TableState(_store);
            const float budget = 4f;
            var goal = new Position(cx + 30f, cz);

            // Park a friendly unit dead-ahead where the centered 4" advance would land its pack.
            var friendlyModels = MakeModels(1, baseRadius: 0.5f);
            friendlyModels[0].GetValue().SetPosition(new Position(cx + 4f, cz));
            var friendly = new UnitData(unit.GetValue().PlayerID, "Blocker", quality: 4, defense: 4,
                modelBindings: friendlyModels);
            _store.Create(friendly);

            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, models, tableState,
                goal, moveBudgetInches: budget, maxDistanceInches: budget,
                budgetFor: _ => new ModelMoveBudget(budget, budget),
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false,
                ignoresImpassibleTerrain: false);

            Assert.That(MaxPathLength(move), Is.LessThanOrEqualTo(4.001f));
            Assert.That(NetCentroidMove(move, cx, cz), Is.GreaterThan(2.5f),
                "a friendly on the arrival spot must cost a side-step, not most of the advance");
        }

        // --- #256 S4: corridor width. The endpoint packers place flank slots geometrically, so a
        // formation wider than a wall corridor fails "moves through impassible" at every arc and the
        // ladder halves to a crawl (the walled Battle Brothers pocket needed a 0.19" arc of a 12"
        // budget). The snake fallback puts every destination ON the pathfound polyline - clear
        // wherever the route is clear for one base - at the cost of stretching the formation.

        [Test]
        public void PlanMoveToward_WallCorridorNarrowerThanFormation_SnakesThroughInsteadOfCrawling()
        {
            // A 2"-wide corridor at x in [10,12] between two walls; a 6-model unit (~3.3" wide as a
            // grid pack) south of it must travel north through the gap.
            var models = MakeModels(6, baseRadius: 0.5f);
            DataBinding<UnitData> unit = MakeUnit(models);
            (float cx, float cz) = Centroid(models);
            var tableState = new TableState(_store);
            // Walls flanking a gap directly north of the unit's centroid (models sit at x 20-22,
            // z 20-21 from MakeModels; the corridor is at x [cx-1, cx+1]).
            _store.Create(new TerrainData(ETerrainType.Impassible,
                new RectangularZone(cx - 15f, cx - 1f, cz + 3f, cz + 9f)));
            _store.Create(new TerrainData(ETerrainType.Impassible,
                new RectangularZone(cx + 1f, cx + 15f, cz + 3f, cz + 9f)));

            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, models, tableState,
                goal: new Position(cx, cz + 30f), moveBudgetInches: 12f, maxDistanceInches: 12f,
                budgetFor: _ => new ModelMoveBudget(12.001f, 12.001f),
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false,
                ignoresImpassibleTerrain: false);

            bool valid = MovementUtilities.ValidatePaths(move, _ => new ModelMoveBudget(12.001f, 12.001f),
                new List<EnemyModelFootprint>(), false, false, false,
                tableState.Terrain.Objects.ToList(), out _, null, lenientCoherency: true);
            Assert.That(valid, Is.True, "the snake must pass the same validator the stage uses");

            var ends = move.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            float headZ = ends.Max(p => p.z);
            Assert.That(headZ - cz, Is.GreaterThan(3f),
                "the head must actually thread the corridor, not crawl in front of it");
            Assert.That(NetCentroidMove(move, cx, cz), Is.GreaterThan(1f),
                "the unit must gain real ground (a first snake move stretches but still advances)");
        }

        [Test]
        public void BuildSnakeCandidate_BigUnit_WrapsFilesToStayCohesive()
        {
            // 11 models at 1.2" spacing would be a 12" single file - past the 9" all-models rule -
            // so the snake must wrap into parallel files and stay cohesion-valid.
            var models = MakeModels(11, baseRadius: 0.5f);
            DataBinding<UnitData> unit = MakeUnit(models);
            (float cx, float cz) = Centroid(models);
            var path = new List<Position> { new Position(cx, cz), new Position(cx, cz + 30f) };

            List<ModelMoveEntry> move = MovementPlanner.BuildSnakeCandidate(unit, models, path,
                arcLengthInches: 10f, terrain: new List<ITerrain>(), baseRadiusInches: 0.5f,
                maxDistanceInches: 12f);

            Assert.That(MaxPathLength(move), Is.LessThanOrEqualTo(12.001f));
            ApplyMoves(move);
            Assert.That(CohesiveFormation.IsCohesive(models), Is.True,
                "an 11-model snake must wrap into files rather than break the 9\" rule");
        }

        private DataBinding<UnitData> MakeUnit(List<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Blob", quality: 4, defense: 4,
                modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static (float, float) Centroid(List<DataBinding<ModelData>> models) =>
            (models.Average(mb => mb.GetValue().Position.x),
             models.Average(mb => mb.GetValue().Position.z));

        private static float NetCentroidMove(List<ModelMoveEntry> move, float cx, float cz)
        {
            var ends = move.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            float ex = ends.Average(p => p.x), ez = ends.Average(p => p.z);
            return MathF.Sqrt((ex - cx) * (ex - cx) + (ez - cz) * (ez - cz));
        }

        private static float MaxPathLength(List<ModelMoveEntry> move)
        {
            float max = 0f;
            foreach (ModelMoveEntry entry in move)
            {
                Position previous = entry.Model.GetValue().Position;
                float total = 0f;
                foreach (Position p in entry.Positions)
                {
                    total += MathF.Sqrt((p.x - previous.x) * (p.x - previous.x)
                        + (p.z - previous.z) * (p.z - previous.z));
                    previous = p;
                }
                max = Math.Max(max, total);
            }
            return max;
        }

        private List<DataBinding<ModelData>> MakeModels(int count, float baseRadius)
        {
            var bindings = new List<DataBinding<ModelData>>(count);
            for (int i = 0; i < count; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: baseRadius,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(20f + (i % 3), 20f + (i / 3)),
                    gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            return bindings;
        }

        private static void ApplyMoves(List<ModelMoveEntry> moves)
        {
            foreach (ModelMoveEntry move in moves)
                move.Model.GetValue().SetPosition(move.Positions[^1]);
        }

        private static float Span(List<DataBinding<ModelData>> models, Func<ModelData, float> axis)
        {
            var values = models.Select(mb => axis(mb.GetValue())).ToList();
            return values.Max() - values.Min();
        }
    }
}
