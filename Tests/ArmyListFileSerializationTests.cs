using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #059 slice A: the .fdgarmy moved from Newtonsoft (TypeNameHandling) to System.Text.Json so embedded
    // rule definitions ride the same kind-schema. ArmyListFile has List<> members (record equality is
    // reference-based on those), so assert structural round-trip via JSON idempotence + deep spot-checks,
    // and prove a full embedded SpecialRuleDefinition survives inside the army file.
    [TestFixture]
    public class ArmyListFileSerializationTests
    {
        private static ArmyListFile MakeArmy() => new ArmyListFile
        {
            Name = "Test", Faction = "Faction", PointsLimit = 500,
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Warriors", ModelCount = 5, Quality = 4, Defense = 4, PointCost = 150,
                    SpecialRules = new()
                    {
                        new SpecialRuleEntry_Core("Stealth"),
                        new SpecialRuleEntry_CoreNumeric("Tough", 3),
                    },
                    Weapons = new()
                    {
                        new WeaponFileEntry
                        {
                            Name = "Heavy Rifle", RangeInches = 36, Attacks = 1,
                            SpecialRules = new() { new SpecialRuleEntry_CoreNumeric("Blast", 3) },
                        },
                    },
                },
            },
            // Embedded definition: prove a full rule tree rides inside the army file.
            RuleDefinitions = new() { CoreRuleCatalog.Stealth },
        };

        [Test]
        public void FullArmy_WithEmbeddedRules_RoundTripsStructurally()
        {
            ArmyListFile army = MakeArmy();

            string first = JsonSerializer.Serialize(army, RuleJson.Options);
            ArmyListFile back = JsonSerializer.Deserialize<ArmyListFile>(first, RuleJson.Options)!;
            string second = JsonSerializer.Serialize(back, RuleJson.Options);

            Assert.That(second, Is.EqualTo(first), "army did not round-trip structurally.");

            // Spot-checks across the boundary types (polymorphic SpecialRuleEntry + embedded definition).
            Assert.That(back.Name, Is.EqualTo("Test"));
            Assert.That(back.Units, Has.Count.EqualTo(1));
            Assert.That(back.Units[0].SpecialRules[0], Is.EqualTo(new SpecialRuleEntry_Core("Stealth")));
            Assert.That(back.Units[0].SpecialRules[1], Is.EqualTo(new SpecialRuleEntry_CoreNumeric("Tough", 3)));
            Assert.That(back.Units[0].Weapons[0].SpecialRules[0], Is.EqualTo(new SpecialRuleEntry_CoreNumeric("Blast", 3)));
            Assert.That(back.RuleDefinitions, Has.Count.EqualTo(1));
            Assert.That(back.RuleDefinitions[0].Name, Is.EqualTo("Stealth"));
        }

        // #033: the army's spell list rides the same STJ kind-schema (each spell's Effect graph is
        // polymorphic, TargetSelector is plain), embedded in the army file alongside RuleDefinitions.
        [Test]
        public void ArmyWithSpells_RoundTripsStructurally()
        {
            ArmyListFile army = MakeArmy();
            army.Spells = new()
            {
                // Damage spell: AP on its own field, a numeric weapon rule in WithRules.
                new SpellDefinition("Psy-Bolt", 2,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                    new Effect.DealHits(1, new[] { "Blast(3)" }, ArmorPenetration: 2)),
                // Buff spell: grants a rule to up to two friendly units "once".
                new SpellDefinition("Blessing", 1,
                    new TargetSelector(12f, 1, 2, ETargetAffinity.Friend, RequireLineOfSight: false),
                    new Effect.AddRule("Furious", ELifetime.NextTrigger)),
            };

            string first = JsonSerializer.Serialize(army, RuleJson.Options);
            ArmyListFile back = JsonSerializer.Deserialize<ArmyListFile>(first, RuleJson.Options)!;
            string second = JsonSerializer.Serialize(back, RuleJson.Options);

            Assert.That(second, Is.EqualTo(first), "army with spells did not round-trip structurally.");
            Assert.That(back.Spells, Has.Count.EqualTo(2));

            Assert.That(back.Spells[0].Name, Is.EqualTo("Psy-Bolt"));
            Assert.That(back.Spells[0].Threshold, Is.EqualTo(2));
            Assert.That(back.Spells[0].Target.RangeInches, Is.EqualTo(18f));
            Assert.That(back.Spells[0].Target.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
            Assert.That(back.Spells[0].Target.RequireLineOfSight, Is.True);
            Effect.DealHits dealHits = (Effect.DealHits)back.Spells[0].Effect;
            Assert.That(dealHits.Count, Is.EqualTo(1));
            Assert.That(dealHits.ArmorPenetration, Is.EqualTo(2));
            Assert.That(dealHits.WithRules, Does.Contain("Blast(3)"));

            Assert.That(((Effect.AddRule)back.Spells[1].Effect).RuleName, Is.EqualTo("Furious"));
        }

        // Runtime-only members (StableID counter, computed TotalPoints) must not be persisted.
        [Test]
        public void RuntimeOnlyMembers_AreNotPersisted()
        {
            string json = JsonSerializer.Serialize(MakeArmy(), RuleJson.Options).ToLowerInvariant();

            Assert.That(json, Does.Not.Contain("stableid"));
            Assert.That(json, Does.Not.Contain("totalpoints"));
        }
    }
}
