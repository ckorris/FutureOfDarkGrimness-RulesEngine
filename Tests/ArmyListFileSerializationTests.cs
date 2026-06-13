using System.Text.Json;
using FDG.Rules.Dispatch;
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
