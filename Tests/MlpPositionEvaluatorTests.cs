using System.Text.Json;
using FDG.Ai.Tactician.Learning;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #191 step 14. The forward pass is hand-written C# (no ONNX Runtime - plan sec 10), so the
    /// only thing standing between "the model we trained" and "the model we ship" is these tests:
    /// the parity fixture is torch's own output for fixed weights on real feature rows, so layer
    /// order, row-major weights, ReLU and the sigmoid on head 0 are all checked against the trainer
    /// rather than against my own reading of it.
    /// </summary>
    [TestFixture]
    public class MlpPositionEvaluatorTests
    {
        // FDG_MLP_WEIGHTS / FDG_MLP_CASES point this at a freshly exported model instead of the
        // committed fixture, so a REAL candidate's weights can be parity-checked before anyone
        // promotes them (step 15). Unset - always, in CI - the committed fixture is used.
        private static string FixturePath(string name)
        {
            string? overridePath = Environment.GetEnvironmentVariable(
                name.Contains("weights") ? "FDG_MLP_WEIGHTS" : "FDG_MLP_CASES");
            if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
            return Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Tests", "Fixtures", name));
        }

        private static MlpPositionEvaluator LoadFixtureNet() =>
            MlpPositionEvaluator.FromFile(FixturePath("mlp-parity-weights.json"));

        private static void MakeUnit(GameDataStore store, PlayerID owner, int modelCount, float atX, float atZ)
        {
            var weapon = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), store);
                modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{owner}_{atX}", 4, 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = store.GetDataBinding<UnitData>(store.Create(unit));
            store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }) { PointsLimit = 1000 });
        }

        [Test]
        public void ForwardPass_MatchesTorch_OnEveryFixtureCase()
        {
            MlpPositionEvaluator net = LoadFixtureNet();
            using JsonDocument cases = JsonDocument.Parse(File.ReadAllText(FixturePath("mlp-parity-cases.json")));
            JsonElement inputs = cases.RootElement.GetProperty("inputs");
            JsonElement expected = cases.RootElement.GetProperty("expected_value");
            Assert.That(inputs.GetArrayLength(), Is.EqualTo(expected.GetArrayLength()));
            Assert.That(inputs.GetArrayLength(), Is.GreaterThan(8), "a parity fixture needs enough cases to mean something");

            var seen = new List<float>();
            for (int i = 0; i < inputs.GetArrayLength(); i++)
            {
                float[] features = inputs[i].EnumerateArray().Select(v => v.GetSingle()).ToArray();
                float value = net.Value(features);
                seen.Add(value);
                Assert.That(value, Is.EqualTo(expected[i].GetSingle()).Within(1e-5f),
                    $"case {i} disagrees with torch");
            }

            // Guard the FIXTURE, not the code: if a regenerated fixture ever collapsed toward a
            // constant, every assertion above would still pass for an implementation that ignored
            // its input entirely.
            Assert.That(seen.Max() - seen.Min(), Is.GreaterThan(0.2f),
                "the fixture's outputs must span a range, or this test cannot fail for a constant");
        }

        [Test]
        public void ForwardPass_IsFastEnoughForALeaf()
        {
            // The leaf runs ~800 times per activation inside the search budget. The hand evaluator
            // it replaces costs ~1.8ms, nearly all of it in the encoder - so the NET's own cost has
            // to disappear into the noise beside that, not merely be "fast".
            MlpPositionEvaluator net = LoadFixtureNet();
            var features = new float[PositionEncoder.ServingVectorWidth];
            for (int i = 0; i < features.Length; i++) features[i] = 0.5f;
            for (int i = 0; i < 1000; i++) net.Value(features); // JIT warm

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            const int iterations = 20_000;
            for (int i = 0; i < iterations; i++) net.Value(features);
            stopwatch.Stop();

            double perCallMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
            TestContext.WriteLine($"forward pass: {perCallMs * 1000:F1} us per call");
            Assert.That(perCallMs, Is.LessThan(0.1),
                "a leaf evaluator's own arithmetic must not be a measurable share of the search budget");
        }

        [Test]
        public void Weights_FromADifferentSchema_AreRefused()
        {
            string json = File.ReadAllText(FixturePath("mlp-parity-weights.json"))
                .Replace("\"schema\": 3", "\"schema\": 2");
            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(
                () => MlpPositionEvaluator.FromJson(json));
            Assert.That(error!.Message, Does.Contain("schema"),
                "a v2-trained net silently reading v3 features is the failure this guard exists for");
        }

        [Test]
        public void Value_WithTheWrongFeatureCount_Throws()
        {
            MlpPositionEvaluator net = LoadFixtureNet();
            Assert.Throws<ArgumentException>(() => net.Value(new float[PositionEncoder.ServingVectorWidth - 1]));
        }

        [Test]
        public void TwoSideValues_AreComplementary_OnARealBoard()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            var us = new PlayerID(Guid.NewGuid());
            var them = new PlayerID(Guid.NewGuid());
            store.Create(new ObjectiveData(new Position(30f, 24f), store));
            MakeUnit(store, us, 3, 28f, 24f);
            MakeUnit(store, them, 3, 50f, 24f);
            var sides = SideMap.FromSlots(new[] { (us, 0), (them, 1) });

            SideValues values = LoadFixtureNet().Evaluate(tableState, evaluator, sides);
            Assert.That(values.IsComplementaryTwoSide(), Is.True,
                "a zero-sum game's two leaf values must sum to 1 - the search backs up the difference");
            Assert.That(values[0], Is.InRange(0f, 1f));
        }
    }
}
