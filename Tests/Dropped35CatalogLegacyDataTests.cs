using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #436 - the 3.5 catalog rules 3.6 dropped are gone, and a pre-3.6 .fdgarmy that names them still loads (plan
    // rule 6). The same shape as CounterLegacyDataTests (#422): each deleted name is reported once through the #168
    // "not implemented" drop, nothing else on the list is lost, the army still builds, and the army-builder audit
    // agrees with the launch. Core names are referenced, not embedded, so an old file cannot carry its own copy.
    //
    // 3.6 replacements, for the reader: Courage Aura -> Bravery Aura; Harassing / Guerrilla Boost (+ Aura) ->
    // Enhanced Harassing / Guerrilla; Piercing Assault -> Piercing Charge; Piercing Hunter -> Piercing Marksman;
    // Darkborn (Offensive) / (Defensive) -> one Darkborn; Strafing -> Crossing Strike; Resistance -> Spell
    // Resistance (different thresholds); Crack and Storm of X have no rule successor (Storm of X are 3.6 weapons).
    [TestFixture]
    public class Dropped35CatalogLegacyDataTests
    {
        private static readonly string[] DroppedUnitRules =
        {
            "Courage Aura", "Harassing Boost", "Guerrilla Boost", "Harassing Boost Aura", "Guerrilla Boost Aura",
            "Resistance", "Resistance Aura", "Piercing Assault", "Piercing Assault Aura", "Piercing Hunter",
            "Piercing Hunter Aura", "Darkborn (Offensive)", "Darkborn (Defensive)",
            "Storm of Change", "Storm of Lust", "Storm of Plague", "Storm of War",
        };

        private static readonly string[] DroppedWeaponRules = { "Crack", "Strafing" };

        private static IEnumerable<string> AllDropped => DroppedUnitRules.Concat(DroppedWeaponRules);

        [Test]
        public void EveryDropped35Name_IsGoneFromTheCatalog()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();

            foreach (string name in AllDropped)
            {
                Assert.That(resolver.TryResolve(name, out _), Is.False, $"3.6 dropped {name}.");
                Assert.That(CoreRuleCatalog.All.Any(d => d.Name == name), Is.False, $"{name} is out of All too.");
            }
            foreach (string kept in new[] { "Courage", "Harassing", "Guerrilla", "Protected", "Rending", "Melee Evasion",
                         "Shred in Melee", "Crossing Attack" })
            {
                Assert.That(resolver.TryResolve(kept, out _), Is.True, $"{kept} is still a 3.6 catalog rule.");
            }
        }

        [Test]
        public void ALegacyListNamingThem_LoadsWithANotImplementedDiagnosticForEach()
        {
            ArmyListFile army = LegacyList();

            List<RuleDrop> drops = Launch(army, out UnitData unit);

            Assert.That(drops.Select(d => d.RuleName), Is.EquivalentTo(AllDropped),
                "each dropped name is reported once - nothing else on the list is lost.");
            Assert.That(drops.Select(d => d.Reason), Is.All.EqualTo(ERuleDropReason.Unimplemented));
            Assert.That(unit.Name, Is.EqualTo("Raiders"), "the army still builds.");
            Assert.That(unit.RuleDefinitions.Select(r => r.Definition.Name), Does.Contain("Harassing"),
                "the surviving 3.6 rules on the same unit still attach.");
            Assert.That(ArmyRuleAudit.Audit(army).Drops.Select(d => d.RuleName), Is.EquivalentTo(AllDropped),
                "the army-builder audit reports the same drops the launch does.");
        }

        // --- harness ---

        private static ArmyListFile LegacyList()
        {
            var unit = new UnitFileEntry
            {
                Name = "Raiders", ModelCount = 3, Quality = 4, Defense = 4,
                Weapons =
                {
                    new WeaponFileEntry { Name = "Rifle", Quantity = 3, RangeInches = 24, Attacks = 1,
                        SpecialRules = { new SpecialRuleEntry_Core("Crack") } },
                    new WeaponFileEntry { Name = "Bombs", Quantity = 1, RangeInches = 0, Attacks = 3,
                        SpecialRules = { new SpecialRuleEntry_Core("Strafing") } },
                },
            };
            unit.SpecialRules.Add(new SpecialRuleEntry_Core("Harassing"));
            foreach (string name in DroppedUnitRules)
            {
                unit.SpecialRules.Add(new SpecialRuleEntry_Core(name));
            }
            return new ArmyListFile
            {
                Name = "Legacy raiders (saved under 3.5)",
                Faction = "Test",
                Units = { unit },
            };
        }

        /// <summary>Runs the real launch path, capturing structured drops and the built unit.</summary>
        private static List<RuleDrop> Launch(ArmyListFile army, out UnitData unit)
        {
            var drops = new List<RuleDrop>();
            void Capture(RuleDrop drop) => drops.Add(drop);

            RuleDiagnostics.OnRuleDropped += Capture;
            try
            {
                GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
                RuleResolver resolver = CoreRuleCatalog.CreateResolver();
                ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
                GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);

                unit = store.GetAllValues<UnitData>().Single();
            }
            finally
            {
                RuleDiagnostics.OnRuleDropped -= Capture;
            }

            return drops;
        }
    }
}
