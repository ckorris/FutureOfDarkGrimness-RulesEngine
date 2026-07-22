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
