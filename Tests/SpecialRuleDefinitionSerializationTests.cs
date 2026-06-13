using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FDG.Tests
{
    // Step 4: the whole tree ties together. SpecialRuleDefinition and ActivatedAbility carry
    // IReadOnlyList<> members, and C# record equality compares those BY REFERENCE — so a freshly
    // deserialized instance is never == the original even on a perfect round-trip. Assert structural
    // round-trip via JSON idempotence (serialize -> deserialize -> re-serialize, compare the JSON)
    // instead, plus deep spot-checks on members whose own equality is structural.
    [TestFixture]
    public class SpecialRuleDefinitionSerializationTests
    {
        // A hand-built definition exercising the full tree: a passive HookEntry (nested Condition + Effect)
        // and an ActivatedAbility (Cost + TargetSelector + Effect + Condition), plus a non-default Scope.
        [Test]
        public void FullTree_RoundTripsStructurally()
        {
            SpecialRuleDefinition rule = new SpecialRuleDefinition(
                "TestRule",
                Passive: new[]
                {
                    new HookEntry(
                        EHookID.Round_OnRoundStart,
                        new Condition.And(new Condition.UnmodifiedRollEquals(6), new Condition.DistanceGreaterThan(9f)),
                        new Effect.AddExtraHit(6),
                        ELifetime.ThisAttack),
                },
                Activated: new[]
                {
                    new ActivatedAbility(
                        EHookID.Round_OnRoundStart,
                        new Cost.SpellTokens(2),
                        new TargetSelector(6f, 1, 1, ETargetAffinity.Friend, RequireLineOfSight: true),
                        new Effect.Reactivate(),
                        new Condition.Always()),
                },
                Scope: ERuleScope.Weapon);

            string first = JsonSerializer.Serialize(rule, RuleJson.Options);
            SpecialRuleDefinition back = JsonSerializer.Deserialize<SpecialRuleDefinition>(first, RuleJson.Options)!;

            Assert.That(JsonSerializer.Serialize(back, RuleJson.Options), Is.EqualTo(first),
                "structural round-trip (JSON idempotence) failed.");

            // Spot-checks: members whose own equality IS structural (records without collections / scalars).
            Assert.That(back.Name, Is.EqualTo("TestRule"));
            Assert.That(back.Scope, Is.EqualTo(ERuleScope.Weapon));
            Assert.That(back.Passive, Has.Count.EqualTo(1));
            Assert.That(back.Passive[0].Condition, Is.EqualTo(rule.Passive[0].Condition));
            Assert.That(back.Activated[0].Cost, Is.EqualTo(new Cost.SpellTokens(2)));
            Assert.That(back.Activated[0].TargetSelector.TargetAffinity, Is.EqualTo(ETargetAffinity.Friend));
        }

        // The real corpus: every catalog rule must round-trip structurally. This is the proof that the
        // JSON vocabulary actually covers every Condition / Effect / Cost / nested-type shape in live use —
        // the "validate the schema against the actual rules" check, run over all ~27 of them.
        [Test]
        public void EveryCatalogRule_RoundTripsStructurally()
        {
            Assert.That(CoreRuleCatalog.All, Is.Not.Empty);

            foreach (SpecialRuleDefinition rule in CoreRuleCatalog.All)
            {
                string first = JsonSerializer.Serialize(rule, RuleJson.Options);
                SpecialRuleDefinition back = JsonSerializer.Deserialize<SpecialRuleDefinition>(first, RuleJson.Options)!;
                string second = JsonSerializer.Serialize(back, RuleJson.Options);

                Assert.That(second, Is.EqualTo(first), $"Rule '{rule.Name}' did not round-trip structurally.");
            }
        }
    }
}
