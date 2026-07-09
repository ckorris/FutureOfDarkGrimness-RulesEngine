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
