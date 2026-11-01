using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FDG.Tests
{
    // Step 2 of the rule-definition JSON loader: Condition round-trips through System.Text.Json as
    // kind-tagged JSON, including the recursive And/Or/Not cases (which re-enter the same polymorphic
    // converter) and the just-added TargetMajorityHasTough. Also proves the RuleJson type-info modifier
    // strips the engine-only computed RequiredCapabilities property (an IReadOnlyCollection<Type> STJ
    // refuses to serialize) from every contract.
    [TestFixture]
    public class ConditionSerializationTests
    {
        [Test]
        public void Leaf_RoundTrips()
        {
            Condition original = new Condition.DistanceGreaterThan(9f);
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"distanceGreaterThan\""));
            Assert.That(json, Does.Contain("\"distanceInches\": 9"));
            Assert.That(JsonSerializer.Deserialize<Condition>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void NestedAnd_RoundTripsBothBranches()
        {
            Condition original = new Condition.And(
                new Condition.UnmodifiedRollEquals(6),
                new Condition.DistanceGreaterThan(9f));

            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"and\""));
            Assert.That(json, Does.Contain("\"kind\": \"unmodifiedRollEquals\""));
            Assert.That(json, Does.Contain("\"kind\": \"distanceGreaterThan\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(json, RuleJson.Options), Is.EqualTo(original));
        }

        // Guards the subtype that was missing its [JsonDerivedType] line — without it this throws.
        [Test]
        public void TargetMajorityHasTough_RoundTrips()
        {
            Condition original = new Condition.TargetMajorityHasTough(3);
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"targetMajorityHasTough\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(json, RuleJson.Options), Is.EqualTo(original));
        }

        // #428: the 3.6 gate and the strict holdout gate each round-trip under their own kind.
        [Test]
        public void RuleOwnershipGates_RoundTripUnderTheirOwnKinds()
        {
            string most = JsonSerializer.Serialize((Condition)new Condition.MostModelsHaveThisRule(), RuleJson.Options);
            string every = JsonSerializer.Serialize((Condition)new Condition.EveryModelHasThisRule(), RuleJson.Options);

            Assert.That(most, Does.Contain("\"kind\": \"mostModelsHaveThisRule\""));
            Assert.That(every, Does.Contain("\"kind\": \"everyModelHasThisRule\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(most, RuleJson.Options),
                Is.EqualTo(new Condition.MostModelsHaveThisRule()));
            Assert.That(JsonSerializer.Deserialize<Condition>(every, RuleJson.Options),
                Is.EqualTo(new Condition.EveryModelHasThisRule()));
        }

        // #428: pre-3.6 data (old .fdgarmy files, .fdgsave snapshots, the bundled books until #438 re-bakes
        // them) says "allModelsHaveThisRule". It must still parse - nested too, the way Reinforcement and the
        // Grounded family And-compose it - and it parses to the MOST-models gate (3.6 semantics), not to the
        // strict one. It keeps its kind on the way back out, so a re-saved old file is unchanged.
        [Test]
        public void TheLegacyAllModelsKind_ParsesAsTheMostModelsGate_AndRoundTrips()
        {
            const string json = "{ \"kind\": \"and\", \"left\": { \"kind\": \"allModelsHaveThisRule\" }, " +
                "\"right\": { \"kind\": \"always\" } }";

            var parsed = (Condition.And)JsonSerializer.Deserialize<Condition>(json, RuleJson.Options)!;

            Assert.That(parsed.Left, Is.InstanceOf<Condition.LegacyAllModelsHaveThisRule>());
            Assert.That(parsed.Left, Is.InstanceOf<Condition.MostModelsHaveThisRule>(),
                "the legacy kind IS the most-models gate, so every 'is MostModelsHaveThisRule' check accepts it");
            Assert.That(parsed.Left, Is.Not.InstanceOf<Condition.EveryModelHasThisRule>());
            Assert.That(JsonSerializer.Serialize((Condition)parsed, RuleJson.Options),
                Does.Contain("\"kind\": \"allModelsHaveThisRule\""));
        }

        // A no-data condition serializes to just its discriminator.
        [Test]
        public void Always_SerializesToKindOnly()
        {
            string json = JsonSerializer.Serialize((Condition)new Condition.Always(), RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"always\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(json, RuleJson.Options),
                Is.EqualTo(new Condition.Always()));
        }

        // The RuleJson modifier must drop the engine-only RequiredCapabilities property — STJ can't
        // serialize System.Type, and it has no place in the persisted format. Check a subtype that
        // overrides it (And) so a stale base-only ignore wouldn't pass by accident.
        [Test]
        public void RequiredCapabilities_IsNotSerialized()
        {
            Condition withCapabilities = new Condition.And(
                new Condition.AfterMoving(),
                new Condition.IsMelee());

            string json = JsonSerializer.Serialize(withCapabilities, RuleJson.Options);

            Assert.That(json, Does.Not.Contain("apabilit"),
                "RequiredCapabilities must be stripped from every contract, including overrides.");
        }
    }
}
