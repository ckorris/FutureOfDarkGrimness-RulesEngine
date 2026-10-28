using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #407 - a one-model upgrade's Tough lands on that model only. Discord: "both infantry squads with a single
    // crew weapon in the 1000K HDF list have tough 3 applied to all models in the unit, 30 wounds total". The
    // HDF Infantry Squad's "Replace one Rifle and CCW -> Weapon Team (Mortar, Crew, Tough(3))" carries Tough(3)
    // as an ITEM rule, and every item rule folded onto the unit. Now:
    //   compile  - a One/Any section's model-personal rules (Tough) become UnitFileEntry.ModelGrants;
    //   import   - an OPR loadout item whose count is below the unit size does the same;
    //   load     - the grant attaches to the unit's LAST model(s), and UnitCreationRules gives them their own
    //              max wounds, so the squad has 9 x 1 + 1 x 3 = 12 wounds.
    // Unit-wide stays unit-wide: an All section, a single-model unit (a hero's mount - the #006 join reads the
    // hero's Tough off the unit), and every rule other than Tough (owner's call: Tough only, for now).
    [TestFixture]
    public class PerModelToughGrantTests
    {
        private static readonly PlayerID Player = new PlayerID(Guid.NewGuid());

        [Test]
        public void Compile_OneModelWeaponTeam_GrantsToughToOneModel_NotTheUnit()
        {
            UnitFileEntry squad = Compile(BookWith(InfantrySquad()), WeaponTeamChoice());

            Assert.That(squad.SpecialRules.OfType<SpecialRuleEntry_CoreNumeric>().Where(r => r.Name == "Tough"),
                Is.Empty, "the squad itself is not Tough - that was the 30-wound bug");
            Assert.That(squad.ModelGrants, Has.Count.EqualTo(1));
            Assert.That(squad.ModelGrants![0].Count, Is.EqualTo(1), "one Weapon Team, one model");
            Assert.That(squad.ModelGrants[0].Rules, Is.EqualTo(new[] { new SpecialRuleEntry_CoreNumeric("Tough", 3) }));
        }

        [Test]
        public void Compile_AllModelsSection_KeepsToughUnitWide()
        {
            RosterUnit roster = InfantrySquad();
            roster.Sections[0].Affects = UpgradeAffects.All;

            UnitFileEntry squad = Compile(BookWith(roster), WeaponTeamChoice());

            Assert.That(squad.SpecialRules, Does.Contain(new SpecialRuleEntry_CoreNumeric("Tough", 3)),
                "every model bought it, so it is the unit's");
            Assert.That(squad.ModelGrants, Is.Null);
        }

        [Test]
        public void Compile_SingleModelUnit_KeepsToughUnitWide()
        {
            // A hero buying a mount is the bulk of the 294 corpus sites; one model IS the unit, and the #006
            // join reads the hero's Tough from its unit rules.
            RosterUnit roster = InfantrySquad();
            roster.BaseModelCount = roster.MinModels = roster.MaxModels = 1;
            roster.Weapons.ForEach(w => w.Quantity = 1);

            UnitFileEntry hero = Compile(BookWith(roster), WeaponTeamChoice());

            Assert.That(hero.SpecialRules, Does.Contain(new SpecialRuleEntry_CoreNumeric("Tough", 3)));
            Assert.That(hero.ModelGrants, Is.Null);
        }

        [Test]
        public void Compile_OtherItemRules_StillFoldOntoTheUnit()
        {
            RosterUnit roster = InfantrySquad();
            roster.Sections[0].Options[0].ItemsGained[0].Rules.Add(new SpecialRuleEntry_Core("Fear"));

            UnitFileEntry squad = Compile(BookWith(roster), WeaponTeamChoice());

            Assert.That(squad.SpecialRules, Does.Contain(new SpecialRuleEntry_Core("Fear")),
                "only Tough is model-carried today; the rest is follow-up work");
            Assert.That(squad.ModelGrants![0].Rules, Has.Count.EqualTo(1));
        }

        [Test]
        public void Compile_CombinedPair_KeepsEachHalfsWeaponTeam()
        {
            BookFile book = BookWith(InfantrySquad());
            var list = new BuilderList
            {
                Name = "Test", BookName = book.Name, PointsLimit = 2000,
                Units =
                {
                    new BuilderUnit { RosterUnitId = "squad", Id = "a", ModelCount = 10, Choices = { WeaponTeamChoice() } },
                    new BuilderUnit { RosterUnitId = "squad", Id = "b", ModelCount = 10, CombinedWithId = "a",
                        Choices = { WeaponTeamChoice() } },
                },
            };

            UnitFileEntry merged = ListCompiler.Compile(book, list).Units.Single();

            Assert.That(merged.ModelCount, Is.EqualTo(20));
            Assert.That(merged.ModelGrants!.Sum(g => g.Count), Is.EqualTo(2), "two Weapon Teams in the merged unit");
        }

        [Test]
        public void ArmyLoad_GivesOnlyTheLastModelItsTough()
        {
            BuiltArmyFile army = ListCompiler.Compile(BookWith(InfantrySquad()), ListWith(WeaponTeamChoice()));
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameBootstrap.CreateArmy(Player, army, store, CoreRuleCatalog.CreateResolver());
            UnitData squad = store.GetAllValues<UnitData>().Single();

            UnitCreationRules.Apply(squad, new RuleEvaluator(new FixedDiceRoller(4)));

            List<IModel> models = squad.Models;
            Assert.That(models.Take(9).Select(m => m.TotalWounds), Is.All.EqualTo(1f));
            Assert.That(models[9].TotalWounds, Is.EqualTo(3f),
                "the Weapon Team sits last, so the automatic wound order reaches it last");
            Assert.That(squad.RemainingWounds, Is.EqualTo(12f), "9 x 1 + 1 x 3, not 30");
            Assert.That(TransportUtilities.GetUnitSpaceCost(squad), Is.EqualTo(12),
                "a non-hero Tough(3) model takes 3 transport spaces; the rest take 1");
        }

        [Test]
        public void ArmyFile_RoundTripsTheGrant_AndOmitsItWhenAbsent()
        {
            BuiltArmyFile army = ListCompiler.Compile(BookWith(InfantrySquad()), ListWith(WeaponTeamChoice()));
            string json = JsonSerializer.Serialize<ArmyListFile>(army, RuleJson.Options);
            ArmyListFile back = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)!;

            Assert.That(back.Units.Single().ModelGrants![0].Rules,
                Is.EqualTo(new[] { new SpecialRuleEntry_CoreNumeric("Tough", 3) }));

            BuiltArmyFile plain = ListCompiler.Compile(BookWith(InfantrySquad()), ListWith());
            Assert.That(JsonSerializer.Serialize<ArmyListFile>(plain, RuleJson.Options), Does.Not.Contain("ModelGrants"),
                "a unit without grants writes exactly what it wrote before #407");
        }

        [Test]
        public void OprImport_LoadoutItemOnFewerModels_BecomesAModelGrant()
        {
            ArmyListFile army = OprListImporter.Import(ImportJson, ImportBooks).Army;
            UnitFileEntry squad = army.Units.Single();

            Assert.That(squad.SpecialRules.OfType<SpecialRuleEntry_CoreNumeric>().Where(r => r.Name == "Tough"), Is.Empty);
            Assert.That(squad.ModelGrants!.Single().Count, Is.EqualTo(1));
            Assert.That(squad.ModelGrants.Single().Rules, Is.EqualTo(new[] { new SpecialRuleEntry_CoreNumeric("Tough", 3) }));
        }

        // ---- Fixtures --------------------------------------------------------------------------------------

        // An Army Forge share list for the same squad: `loadout` counts are unit totals, so the Weapon Team
        // item's count of 1 on a size-10 unit is the one model that bought it.
        private const string ImportJson = """
        {
          "id": "L", "name": "HDF", "gameSystem": "gf", "pointsLimit": 1000, "listPoints": 130,
          "units": [
            { "id": "squad", "cost": 130, "name": "Infantry Squad", "size": 10, "bases": { "round": "25" },
              "rules": [], "quality": 5, "defense": 5, "armyId": "hdf", "combined": false,
              "joinToUnit": null, "selectionId": "S1",
              "loadout": [
                { "type": "ArmyBookWeapon", "name": "Rifle", "count": 9, "range": 24, "attacks": 1, "specialRules": [] },
                { "type": "ArmyBookWeapon", "name": "Mortar", "count": 1, "range": 30, "attacks": 1, "specialRules": [] },
                { "type": "ArmyBookItem", "name": "Weapon Team", "count": 1,
                  "content": [ {"type":"ArmyBookRule","name":"Tough","rating":3} ] }
              ] }
          ],
          "forceOrgErrors": []
        }
        """;

        private static readonly Dictionary<string, string> ImportBooks = new()
        {
            ["hdf"] = """{"uid":"hdf","name":"Human Defense Force","versionString":"3.5.0","units":[]}""",
        };

        private static UpgradeChoice WeaponTeamChoice() =>
            new UpgradeChoice { SectionId = "team-section", OptionId = "mortar-team", Count = 1 };

        private static BuilderList ListWith(params UpgradeChoice[] choices)
        {
            var unit = new BuilderUnit { RosterUnitId = "squad", ModelCount = 10 };
            unit.Choices.AddRange(choices);
            return new BuilderList { Name = "Test", BookName = "Test Book", PointsLimit = 1000, Units = { unit } };
        }

        private static UnitFileEntry Compile(BookFile book, UpgradeChoice choice)
        {
            BuilderList list = ListWith(choice);
            list.Units[0].ModelCount = book.Units[0].BaseModelCount;
            return ListCompiler.Compile(book, list).Units.Single();
        }

        private static BookFile BookWith(RosterUnit unit) => new()
        {
            Name = "Test Book", Faction = "Testers", Units = { unit },
        };

        /// <summary>The HDF Infantry Squad, trimmed to its Weapon Team swap (authored shape as shipped).</summary>
        private static RosterUnit InfantrySquad() => new()
        {
            Id = "squad", Name = "Infantry Squad",
            Quality = 5, Defense = 5,
            BaseModelCount = 10, MinModels = 10, MaxModels = 10, BasePointCost = 100,
            Weapons =
            {
                new WeaponFileEntry { Name = "Rifle", Quantity = 10, RangeInches = 24, Attacks = 1 },
                new WeaponFileEntry { Name = "CCW", Quantity = 10, RangeInches = 0, Attacks = 1 },
            },
            Sections =
            {
                new UpgradeSection
                {
                    Id = "team-section", Label = "Replace one Rifle and CCW",
                    Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One,
                    Targets = { "Rifle", "CCW" },
                    Options =
                    {
                        new UpgradeOption
                        {
                            Id = "mortar-team", Label = "Weapon Team (Mortar, Crew (A2), Tough(3))", Cost = 30,
                            WeaponsGained =
                            {
                                new WeaponFileEntry { Name = "Mortar", Quantity = 1, RangeInches = 30, Attacks = 1 },
                                new WeaponFileEntry { Name = "Crew", Quantity = 1, RangeInches = 0, Attacks = 2 },
                            },
                            ItemsGained =
                            {
                                new ItemEntry
                                {
                                    Name = "Weapon Team", Quantity = 1,
                                    Rules = { new SpecialRuleEntry_CoreNumeric("Tough", 3) },
                                },
                            },
                        },
                    },
                },
            },
        };
    }
}
