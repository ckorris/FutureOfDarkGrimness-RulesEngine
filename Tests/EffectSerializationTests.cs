using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FDG.Tests
{
    // Step 3 of the rule-definition JSON loader: Effect round-trips through System.Text.Json, including
    // each nested type decorated this step — ValueSource (already polymorphic), RerollCondition,
    // TokenClearTrigger, and DiceExpression. Each case deliberately nests one so the recursion through
    // the registered converters is actually exercised, not just the top-level Effect dispatch.
    [TestFixture]
    public class EffectSerializationTests
    {
        private static T RoundTrip<T>(T value)
        {
            string json = JsonSerializer.Serialize(value, RuleJson.Options);
            return JsonSerializer.Deserialize<T>(json, RuleJson.Options)!;
        }

        [Test]
        public void MultiplyWounds_NestsValueSource()
        {
            Effect original = new Effect.MultiplyWounds(new ValueSource.Arg(0));
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"multiplyWounds\""));
            Assert.That(json, Does.Contain("\"kind\": \"arg\""));
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void Reroll_NestsRerollCondition()
        {
            Effect original = new Effect.Reroll(ERollKind.Save, new RerollCondition.AllFailures());
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"reroll\""));
            Assert.That(json, Does.Contain("\"kind\": \"allFailures\""));
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void GrantToken_NestsTokenTypeValueSourceAndClearTrigger()
        {
            Effect original = new Effect.GrantToken(
                TokenType.SpellTokens, new ValueSource.Literal(2), new TokenClearTrigger.RoundEnd());

            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"grantToken\""));
            Assert.That(json, Does.Contain("\"kind\": \"roundEnd\""));
            Assert.That(json, Does.Contain("\"kind\": \"literal\""));
            Assert.That(json, Does.Contain("SpellTokens")); // TokenType serialized as its plain record-struct Id
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(original));
        }

        // Heal nests DiceExpression, whose computed Sides property must be stripped by the modifier.
        [Test]
        public void Heal_NestsDiceExpression_WithoutSides()
        {
            Effect original = new Effect.Heal(new DiceExpression.D3());
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"d3\""));
            Assert.That(json, Does.Not.Contain("sides"), "DiceExpression.Sides is derived from the kind tag and must not be serialized.");
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void NoDataEffect_SerializesToKindOnly()
        {
            string json = JsonSerializer.Serialize((Effect)new Effect.StrikeFirst(), RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"strikeFirst\""));
            Assert.That(json, Does.Not.Contain("apabilit"), "RequiredCapabilities must be stripped from Effects too.");
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(new Effect.StrikeFirst()));
        }
    }
}
