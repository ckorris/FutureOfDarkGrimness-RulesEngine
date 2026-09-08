using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #191 C4 dev override: <c>FDG_STRATEGIST_WEIGHTS</c> swaps the Strategist's leaf for a
    /// learned <see cref="MlpPositionEvaluator"/> without touching the default. The tests set a
    /// process-wide environment variable, so they never run in parallel with each other.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class StrategistWeightsOverrideTests
    {
        private static string FixturePath(string name) => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Tests", "Fixtures", name));

        private string? _saved;

        [SetUp]
        public void SaveVariable() =>
            _saved = Environment.GetEnvironmentVariable(AiProfileFactory.StrategistWeightsEnvVar);

        [TearDown]
        public void RestoreVariable() =>
            Environment.SetEnvironmentVariable(AiProfileFactory.StrategistWeightsEnvVar, _saved);

        private static TacticianPlanner BuildStrategist(Action<string>? decisionLog = null)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            AiProfileFactory.BuildRegistry(EAiProfile.Strategist, tableState, new PlayerID(Guid.NewGuid()),
                out TacticianPlanner? planner, seed: 1, decisionLog: decisionLog);
            Assert.That(planner, Is.Not.Null);
            return planner!;
        }

        [Test]
        public void Unset_TheLeafIsTheShippedNet()
        {
            // Was HandWeightedEvaluator until #191 step 15b (2026-09-07): the shipped net REPLACED
            // the hand leaf as the Strategist's default after the C4 slice measured it ahead at
            // both budgets. The variable's job is now to try a candidate over that default.
            Environment.SetEnvironmentVariable(AiProfileFactory.StrategistWeightsEnvVar, null);

            TacticianPlanner planner = BuildStrategist();

            Assert.That(planner.SearchLeaf, Is.SameAs(AiProfileFactory.DefaultStrategistLeaf),
                "with no override the Strategist scores leaves with the shipped learned net");
        }

        [Test]
        public void PointedAtTheParityFixture_TheLeafIsTheLearnedNet_AndOneLineSaysSo()
        {
            string path = FixturePath("mlp-parity-weights.json");
            Environment.SetEnvironmentVariable(AiProfileFactory.StrategistWeightsEnvVar, path);
            var log = new List<string>();

            TacticianPlanner planner = BuildStrategist(log.Add);

            Assert.That(planner.SearchLeaf, Is.InstanceOf<MlpPositionEvaluator>(),
                "the override replaces the leaf with the net named by the variable");
            Assert.That(log.Count(line => line.Contains(AiProfileFactory.StrategistWeightsEnvVar)), Is.EqualTo(1),
                "exactly one line names the weights file that loaded");
            Assert.That(log.Single(line => line.Contains(AiProfileFactory.StrategistWeightsEnvVar)),
                Does.Contain("mlp-parity-weights.json"));
        }

        [Test]
        public void ACallerSuppliedLeafWinsOverTheVariable()
        {
            Environment.SetEnvironmentVariable(AiProfileFactory.StrategistWeightsEnvVar,
                FixturePath("mlp-parity-weights.json"));
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            var supplied = new HandWeightedEvaluator();

            AiProfileFactory.BuildRegistry(EAiProfile.Strategist, tableState, new PlayerID(Guid.NewGuid()),
                out TacticianPlanner? planner, seed: 1, evaluator: supplied);

            Assert.That(planner!.SearchLeaf, Is.SameAs(supplied),
                "the variable only fills an EMPTY slot - FdgLab's explicit arms are never overridden. "
                + "This is also how the lab still gets a HAND-leaf Strategist for the C-gate's control "
                + "arm now that the shipped default is a net: pass one explicitly.");
        }

        [Test]
        public void AMissingFile_KeepsTheDefaultAndSaysSo()
        {
            string path = FixturePath("no-such-weights.json");
            Environment.SetEnvironmentVariable(AiProfileFactory.StrategistWeightsEnvVar, path);
            var log = new List<string>();

            TacticianPlanner planner = BuildStrategist(log.Add);

            Assert.That(planner.SearchLeaf, Is.SameAs(AiProfileFactory.DefaultStrategistLeaf));
            Assert.That(log, Has.Some.Contains("missing file"));
        }

        [Test]
        public void AFileThatIsNotANet_ThrowsInsteadOfFallingBack()
        {
            string path = FixturePath("mlp-parity-cases.json");
            Environment.SetEnvironmentVariable(AiProfileFactory.StrategistWeightsEnvVar, path);

            Assert.That(() => BuildStrategist(), Throws.Exception,
                "a bad weights file is a loud failure, never a silent hand-weighted game");
        }
    }
}
