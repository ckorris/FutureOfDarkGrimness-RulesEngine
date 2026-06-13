using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #059 slice A: SpecialRuleEntry (the rule *reference* type inside .fdgarmy) is now a System.Text.Json
    // polymorphic hierarchy so the army file can move off Newtonsoft. No collections, so plain record
    // value-equality round-trips work. PrintableName is derived and [JsonIgnore]'d — recomputed on load.
    [TestFixture]
    public class SpecialRuleEntrySerializationTests
    {
        [Test]
        public void Core_RoundTrips_WithoutPersistingPrintableName()
        {
            SpecialRuleEntry original = new SpecialRuleEntry_Core("Stealth");
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"core\""));
            Assert.That(json.ToLowerInvariant(), Does.Not.Contain("printablename"),
                "the derived display name must not be persisted.");
            Assert.That(JsonSerializer.Deserialize<SpecialRuleEntry>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void CoreNumeric_RoundTrips()
        {
            SpecialRuleEntry original = new SpecialRuleEntry_CoreNumeric("Tough", 3);
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"coreNumeric\""));
            Assert.That(json, Does.Contain("\"numericValue\": 3"));
            Assert.That(JsonSerializer.Deserialize<SpecialRuleEntry>(json, RuleJson.Options), Is.EqualTo(original));
        }

        // The recursive case: Alias nests another SpecialRuleEntry, and its PrintableName is computed from
        // the inner entry's — so a correct round-trip proves both the recursion and the ctor recompute.
        [Test]
        public void Alias_NestsInnerEntry_AndRecomputesPrintableName()
        {
            SpecialRuleEntry original = new SpecialRuleEntry_Alias(
                "Medical Training", new SpecialRuleEntry_Core("Regeneration"));
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"alias\""));
            Assert.That(json, Does.Contain("\"kind\": \"core\""));

            SpecialRuleEntry back = JsonSerializer.Deserialize<SpecialRuleEntry>(json, RuleJson.Options)!;
            Assert.That(back, Is.EqualTo(original));
            Assert.That(back.PrintableName, Is.EqualTo("Medical Training (Regeneration)"),
                "PrintableName isn't serialized but the ctor chain rebuilds it on load.");
        }
    }
}
