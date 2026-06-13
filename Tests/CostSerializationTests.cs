using System.Text.Json;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FDG.Tests
{
    // Step 4: Cost is the last closed sum type, nested inside ActivatedAbility. No collections, so plain
    // record value-equality round-trips work here (unlike the SpecialRuleDefinition top level).
    [TestFixture]
    public class CostSerializationTests
    {
        [Test]
        public void NoData_SerializesToKindOnly()
        {
            Cost original = new Cost.OncePerGame();
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"oncePerGame\""));
            Assert.That(JsonSerializer.Deserialize<Cost>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void SpellTokens_RoundTrips()
        {
            Cost original = new Cost.SpellTokens(3);
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"spellTokens\""));
            Assert.That(json, Does.Contain("\"count\": 3"));
            Assert.That(JsonSerializer.Deserialize<Cost>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void ConsumesToken_NestsTokenType()
        {
            Cost original = new Cost.ConsumesToken(TokenType.SpellTokens, 2);
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"consumesToken\""));
            Assert.That(json, Does.Contain("SpellTokens"));
            Assert.That(JsonSerializer.Deserialize<Cost>(json, RuleJson.Options), Is.EqualTo(original));
        }
    }
}
