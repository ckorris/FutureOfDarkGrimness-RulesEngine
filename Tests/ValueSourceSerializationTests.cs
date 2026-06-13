using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FDG.Tests
{
    // Step 1 of the rule-definition JSON loader: proves the smallest polymorphic hierarchy
    // (ValueSource = Literal | Arg) round-trips through System.Text.Json as clean kind-tagged
    // JSON. Serializing the BASE type is what triggers STJ's polymorphic path and emits "kind";
    // the [JsonDerivedType] tags on the record decouple the persisted vocabulary from the C#
    // type names. Template for the larger Condition/Effect converters that follow.
    [TestFixture]
    public class ValueSourceSerializationTests
    {
        [Test]
        public void Literal_RoundTrips()
        {
            ValueSource original = new ValueSource.Literal(3);
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"literal\""));
            Assert.That(json, Does.Contain("\"value\": 3"));
            Assert.That(JsonSerializer.Deserialize<ValueSource>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void Arg_RoundTrips()
        {
            ValueSource original = new ValueSource.Arg(0);
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"arg\""));
            Assert.That(json, Does.Contain("\"index\": 0"));
            Assert.That(JsonSerializer.Deserialize<ValueSource>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void UnknownKind_Throws() =>
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<ValueSource>("{\"kind\":\"bogus\"}", RuleJson.Options));
    }
}
