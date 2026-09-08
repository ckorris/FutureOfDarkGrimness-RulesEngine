using System.Text.Json;
using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Learning;
using FDG.Ai.Tactician.Search;
using FDG.BuiltInAssets;
using FDG.Data;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #191 step 15b: the Strategist ships a learned leaf. These tests guard the ARTIFACT and the
    /// swap - that the embedded weights still match what this build encodes, that they still
    /// compute what torch computed, and that the swap reached the Strategist and only the
    /// Strategist. The forward pass itself is covered by <see cref="MlpPositionEvaluatorTests"/>.
    /// </summary>
    [TestFixture]
    public class StrategistLeafAssetTests
    {
        private static TacticianPlanner BuildProfile(EAiProfile profile)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            AiProfileFactory.BuildRegistry(profile, tableState, new PlayerID(Guid.NewGuid()),
                out TacticianPlanner? planner, seed: 1);
            Assert.That(planner, Is.Not.Null, $"{profile} should build a planner");
            return planner!;
        }

        [Test]
        public void TheEmbeddedAsset_LoadsAndMatchesThisBuildsFeatureSchema()
        {
            // The early warning for a feature bump that lands without a retrain: the encoder and
            // this asset are committed together, so they can only disagree in a half-finished tree.
            // Failing here is what stops that reaching a game, where it would throw at bot creation.
            Assert.That(() => AiProfileFactory.DefaultStrategistLeaf, Throws.Nothing);

            using JsonDocument asset = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(
                BuiltInAssetHelper.GetEmbeddedResource(BuiltInAssetHelper.STRATEGIST_LEAF_PATH)));
            Assert.That(asset.RootElement.GetProperty("schema").GetInt32(),
                Is.EqualTo(PositionEncoder.SchemaVersion), "shipped weights vs this build's feature schema");
            Assert.That(asset.RootElement.GetProperty("features").GetArrayLength(),
                Is.EqualTo(PositionEncoder.ServingVectorWidth), "shipped weights vs this build's serving width");
            Assert.That(asset.RootElement.TryGetProperty("provenance", out _), Is.True,
                "a shipped model carries the record of what trained it (G9)");
        }

        [Test]
        public void TheEmbeddedAsset_StillComputesWhatTorchComputed()
        {
            var net = (MlpPositionEvaluator)AiProfileFactory.DefaultStrategistLeaf;
            using JsonDocument cases = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..",
                "Tests", "Fixtures", "strategist-leaf-v1-parity.json"))));
            JsonElement inputs = cases.RootElement.GetProperty("inputs");
            JsonElement expected = cases.RootElement.GetProperty("expected_value");
            Assert.That(inputs.GetArrayLength(), Is.EqualTo(expected.GetArrayLength()));
            Assert.That(inputs.GetArrayLength(), Is.GreaterThan(8), "too few cases to mean anything");

            var seen = new List<float>();
            for (int i = 0; i < inputs.GetArrayLength(); i++)
            {
                float[] features = inputs[i].EnumerateArray().Select(v => v.GetSingle()).ToArray();
                float value = net.Value(features);
                seen.Add(value);
                Assert.That(value, Is.EqualTo(expected[i].GetSingle()).Within(1e-5f),
                    $"case {i}: the shipped asset no longer agrees with the torch model it came from");
            }

            // Guards the FIXTURE: a collapsed one would pass every assertion above for a net that
            // ignored its input entirely.
            Assert.That(seen.Max() - seen.Min(), Is.GreaterThan(0.2f),
                "the fixture's outputs must span a range, or this test cannot fail");
        }

        [Test]
        public void TheStrategistsDefaultLeaf_IsTheShippedNet_AndIsLoadedOnce()
        {
            TacticianPlanner first = BuildProfile(EAiProfile.Strategist);
            TacticianPlanner second = BuildProfile(EAiProfile.Strategist);

            Assert.That(first.SearchLeaf, Is.InstanceOf<MlpPositionEvaluator>());
            Assert.That(first.SearchLeaf, Is.SameAs(second.SearchLeaf),
                "one shared instance: a bench builds a registry per slot per game, and re-parsing "
                + "400 KB of weights each time would cost more than the search it feeds");
        }

        [Test]
        public void ThePlainTactician_StillScoresWithTheHandEvaluator()
        {
            // The Tactician is the benchmark OPPONENT and the policy the search simulates with.
            // If the promotion had reached it, every number the campaign is calibrated against
            // would have moved silently underneath us.
            TacticianPlanner planner = BuildProfile(EAiProfile.Tactician);

            Assert.That(planner.SearchLeaf, Is.Null.Or.InstanceOf<HandWeightedEvaluator>(),
                "a plain Tactician runs no search and must keep the hand-weighted scoring");
        }
    }
}
