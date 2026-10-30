using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #424 Assault through the data path: the two importers and the list compiler keep ONE entry per Assault
    // weapon (Army Forge stores Assault as an ordinary weapon special rule, so a 3.6 book carries it in
    // SpecialRules with no importer change), the compiled entry builds a weapon that sits in both pools, and -
    // the legacy half - a 3.5 army with no Assault anywhere classifies exactly as it did before the seam widened.
    [TestFixture]
    public class AssaultImportCompileTests
    {
        private const string BookJson = """
        {
          "name": "Test Chapter", "versionString": "3.6.0",
          "units": [
            { "id": "u1", "name": "Pistoleers", "size": 5, "cost": 100, "quality": 4, "defense": 4,
              "weapons": [
                { "name": "Heavy Pistol", "count": 5, "range": 12, "attacks": 1,
                  "specialRules": [{"name":"AP","rating":1},{"name":"Assault"}] },
                { "name": "CCW", "count": 5, "range": null, "attacks": 1, "specialRules": [] }
              ],
              "rules": [], "items": [], "upgrades": [] }
          ],
          "upgradePackages": []
        }
        """;

        [Test]
        public void BookImport_AnAssaultPistol_IsOneWeaponEntry_CarryingTheRule()
        {
            BookFile book = OprBookImporter.Import(BookJson, "TestSource", "CC-BY-SA 4.0");

            List<WeaponFileEntry> pistols = book.Units.Single().Weapons.Where(w => w.Name == "Heavy Pistol").ToList();
            Assert.That(pistols, Has.Count.EqualTo(1), "one entry - never a ranged copy plus a range-0 copy");
            Assert.That(pistols[0].RangeInches, Is.EqualTo(12));
            Assert.That(pistols[0].ArmorPenetration, Is.EqualTo(1), "AP still folds into the number");
            Assert.That(pistols[0].SpecialRules, Is.EqualTo(new SpecialRuleEntry[] { new SpecialRuleEntry_Core("Assault") }),
                "Assault rides SpecialRules like any other weapon rule");
        }

        [Test]
        public void ListImport_AnAssaultPistol_IsOneWeaponEntry_CarryingTheRule()
        {
            const string listJson = """
            {
              "id": "T", "name": "Test List", "gameSystem": "gf", "pointsLimit": 500, "listPoints": 100,
              "units": [
                { "id": "u1", "cost": 100, "name": "Pistoleers", "size": 5, "rules": [], "quality": 4, "defense": 4,
                  "armyId": "bookA", "combined": false, "joinToUnit": null, "selectionId": "S1", "selectedUpgrades": [],
                  "loadout": [
                    { "type": "ArmyBookWeapon", "name": "Heavy Pistol", "count": 5, "range": 12, "attacks": 1,
                      "specialRules": [ {"name":"AP","rating":1}, {"name":"Assault"} ] },
                    { "type": "ArmyBookWeapon", "name": "CCW", "count": 5, "range": 0, "attacks": 1, "specialRules": [] }
                  ] }
              ],
              "forceOrgErrors": []
            }
            """;
            // The share-link importer's version gate still reads 3.5.x (#437 moves it to 3.6); the weapon mapping under
            // test is version-independent, so the stub book claims 3.5.3 to get past the gate.
            var books = new Dictionary<string, string>
            {
                ["bookA"] = """{"uid":"bookA","name":"Test Chapter","versionString":"3.5.3","units":[]}""",
            };

            ArmyListFile army = OprListImporter.Import(listJson, books).Army;

            UnitFileEntry unit = army.Units.Single();
            WeaponFileEntry pistol = unit.Weapons.Single(w => w.Name == "Heavy Pistol");
            Assert.That(pistol.Quantity, Is.EqualTo(5));
            Assert.That(pistol.RangeInches, Is.EqualTo(12));
            Assert.That(pistol.SpecialRules, Is.EqualTo(new SpecialRuleEntry[] { new SpecialRuleEntry_Core("Assault") }));
        }

        // The compiler merges identical profiles (SameProfile includes the rule list), so a "replace all Rifles"
        // upgrade to Assault pistols lands as one 5-copy entry - and the engine builds it as five Weapon objects
        // that each sit in both of the unit's pools.
        [Test]
        public void Compile_ReplaceAllWithAssaultPistols_IsOneEntry_ThatLoadsIntoBothPools()
        {
            var heavyPistol = new WeaponFileEntry
            {
                Name = "Heavy Pistol", Quantity = 1, RangeInches = 12, Attacks = 1, ArmorPenetration = 1,
                SpecialRules = { new SpecialRuleEntry_Core("Assault") },
            };
            var unit = new RosterUnit
            {
                Id = "squad", Name = "Pistoleers", BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 5, RangeInches = 24, Attacks = 1 } },
                Sections =
                {
                    new UpgradeSection
                    {
                        Id = "all", Label = "Replace all Rifles", Variant = UpgradeVariant.Replace,
                        Affects = UpgradeAffects.All, Targets = { "Rifle" },
                        Options = { new UpgradeOption { Id = "o", Label = "Heavy Pistol", WeaponsGained = { heavyPistol }, Cost = 10 } },
                    },
                },
            };
            var book = new BookFile { Name = "T", Units = { unit }, RuleDefinitions = { AssaultDefinitions.Assault } };
            var builderUnit = new BuilderUnit { RosterUnitId = "squad" };
            builderUnit.Choices.Add(new UpgradeChoice { SectionId = "all", OptionId = "o", Count = 1 });

            BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList { PointsLimit = 100000, Units = { builderUnit } });

            UnitFileEntry compiled = army.Units.Single();
            Assert.That(compiled.Weapons.Select(w => w.Name), Is.EqualTo(new[] { "Heavy Pistol" }), "one entry");
            Assert.That(compiled.Weapons[0].Quantity, Is.EqualTo(5));
            Assert.That(compiled.Weapons[0].SpecialRules, Does.Contain(new SpecialRuleEntry_Core("Assault")));
            Assert.That(army.RuleDefinitions.Select(d => d.Name), Does.Contain("Assault"), "the book's definition is embedded");

            IUnit built = Build(army).Single();
            Assert.That(built.GetRangedWeapons(), Has.Count.EqualTo(5));
            Assert.That(built.GetMeleeWeapons(), Has.Count.EqualTo(5));
            Assert.That(built.GetRangedWeapons()[0], Is.SameAs(built.GetMeleeWeapons()[0]), "the same objects, both pools");
        }

        // --- legacy: a 3.5 army with no Assault anywhere classifies exactly as before ----------------------

        [Test]
        public void Legacy_TheShippedTestArmy_ClassifiesExactlyByRange()
        {
            AssertClassifiesByRangeAlone(TestArmies.MakeRichArmy("Legacy"));
        }

        [Test]
        public void Legacy_TheExampleArmyOnDisk_ClassifiesExactlyByRange()
        {
            string path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "armies", "example-with-rules.fdgarmy"));
            Assume.That(File.Exists(path), Is.True, $"missing canonical example army at {path}");

            ArmyListFile army = JsonSerializer.Deserialize<ArmyListFile>(File.ReadAllText(path), RuleJson.Options)!;

            AssertClassifiesByRangeAlone(army);
        }

        private static void AssertClassifiesByRangeAlone(ArmyListFile army)
        {
            Assume.That(army.RuleDefinitions.Any(d => d.Name == "Assault"), Is.False, "a pre-3.6 file names no Assault");
            Assume.That(army.Units.SelectMany(u => u.Weapons).SelectMany(w => w.SpecialRules).Any(r => r.PrintableName == "Assault"),
                Is.False);

            List<IUnit> units = Build(army);
            Assert.That(units, Is.Not.Empty);
            foreach (IUnit unit in units)
            {
                List<Weapon> all = unit.AllWeapons(_ => true);
                List<Weapon> ranged = unit.GetRangedWeapons();
                List<Weapon> melee = unit.GetMeleeWeapons();

                Assert.That(ranged.Select(w => w.Name), Is.EquivalentTo(
                    all.Where(w => w.RangeInches > 0f && !StrafingRules.IsStrafeOnly(w)).Select(w => w.Name)),
                    $"{unit.Name}: the ranged pool is exactly the range > 0 weapons, as before");
                Assert.That(melee.Select(w => w.Name), Is.EquivalentTo(
                    all.Where(w => w.RangeInches <= 0f && !StrafingRules.IsStrafeOnly(w)).Select(w => w.Name)),
                    $"{unit.Name}: the melee pool is exactly the range 0 weapons, as before");
                Assert.That(ranged.Intersect(melee), Is.Empty, $"{unit.Name}: no weapon is in both pools");
                foreach (Weapon weapon in all)
                {
                    Assert.That(weapon.GrantedRoles, Is.EqualTo(EWeaponRoles.None), $"{weapon.Name}: no role grant without Assault");
                }
            }
        }

        private static List<IUnit> Build(ArmyListFile army)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
            GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);
            return store.GetAllValues<UnitData>().Cast<IUnit>().ToList();
        }
    }
}
