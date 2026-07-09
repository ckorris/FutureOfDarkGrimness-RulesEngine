using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 slice 0. Wargear is a rule-bundle: ListCompiler flattens an item's rules into the unit's rule
    // list, which is how 157 references to already-implemented WEAPON rules ended up named at UNIT scope and
    // were dropped by ArmyListRuleResolution's scope gate. Two paths now carry them to the right place, and
    // which path applies is decided by whether the upgrade names a target weapon:
    //
    //   targeted   ("Upgrade all Pulse Rifles with: Drone Controller (Reliable, Takedown)")
    //              -> ListCompiler attaches the rules to that weapon and to nothing else.
    //   untargeted ("Toxic Cysts (Bane in Melee)")
    //              -> folds onto the unit; army-load spreads it across every weapon the unit carries,
    //                 and the rule's own isMelee gate picks the right ones at dispatch.
    //
    // These tests assert WHICH weapon carries WHICH rule. Asserting only that the scope warning stopped
    // would pass for an implementation that attached everything everywhere — precisely the bug the targeted
    // path exists to prevent (a Reliable rifle must not make its owner's melee taser hit on 2+).
    [TestFixture]
    public class WeaponScopedWargearRoutingTests
    {
        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _player = new PlayerID(Guid.NewGuid());
        }

        // ---- ListCompiler: targeted upgrades land on the named weapon --------------------------------

        [Test]
        public void TargetedWargearWeaponRules_AttachToTheNamedWeaponOnly()
        {
            // The real shape of DAO Union's Sniper Drones: a shooting weapon the upgrade names, and a melee
            // weapon it must not touch.
            BookFile book = BookWith(SniperDrones());
            UnitFileEntry unit = CompileWithChoice(book, "drones", "controller-section", "controller");

            Assert.That(RuleNames(Weapon(unit, "Pulse Rifle").SpecialRules),
                Is.EquivalentTo(new[] { "Reliable", "Takedown" }),
                "The upgrade targets Pulse Rifles, so its weapon rules ride the Pulse Rifle.");

            Assert.That(Weapon(unit, "Taser").SpecialRules, Is.Empty,
                "The melee Taser is not the upgrade's target and must not hit on 2+.");

            Assert.That(RuleNames(unit.SpecialRules), Does.Not.Contain("Reliable"),
                "A rule placed on its target weapon is not ALSO folded onto the unit.");
            Assert.That(RuleNames(unit.SpecialRules), Does.Not.Contain("Takedown"));
        }

        [Test]
        public void TargetedUpgradeOnPartOfAStack_SplitsTheWeaponEntry()
        {
            // Three rifles, an Affects.One scope: exactly one rifle gets Precise. This is the owner's
            // "a reliable gun and a non-reliable gun" case expressed as a quantity split.
            RosterUnit roster = SniperDrones();
            roster.Sections[0].Affects = UpgradeAffects.One;
            roster.Sections[0].Options[0].ItemsGained[0].Rules = new List<SpecialRuleEntry>
            {
                new SpecialRuleEntry_Core("Precise"),
            };

            UnitFileEntry unit = CompileWithChoice(BookWith(roster), "drones", "controller-section", "controller");

            List<WeaponFileEntry> rifles = unit.Weapons.Where(w => w.Name == "Pulse Rifle").ToList();
            Assert.That(rifles, Has.Count.EqualTo(2), "The stack splits into upgraded and un-upgraded entries.");

            WeaponFileEntry scoped = rifles.Single(w => w.SpecialRules.Count > 0);
            WeaponFileEntry plain = rifles.Single(w => w.SpecialRules.Count == 0);

            Assert.That(scoped.Quantity, Is.EqualTo(1), "Only the one copy the upgrade paid for is Precise.");
            Assert.That(plain.Quantity, Is.EqualTo(2), "The other two rifles keep the unit's base Quality.");
            Assert.That(RuleNames(scoped.SpecialRules), Is.EquivalentTo(new[] { "Precise" }));
        }

        [Test]
        public void TargetedUpgrade_WhoseWeaponIsAbsent_FallsBackToTheUnit()
        {
            // "Upgrade the Master Marksman Carbine with a Scope" bought without the carbine it upgrades.
            // Nothing matches, so the rule folds onto the unit rather than vanishing; army-load then spreads
            // it, preserving the pre-#197 behaviour instead of silently dropping a rule the player paid for.
            RosterUnit roster = SniperDrones();
            roster.Sections[0].Targets = new List<string> { "Master Marksman Carbines" };

            UnitFileEntry unit = CompileWithChoice(BookWith(roster), "drones", "controller-section", "controller");

            Assert.That(Weapon(unit, "Pulse Rifle").SpecialRules, Is.Empty);
            Assert.That(RuleNames(unit.SpecialRules), Is.SupersetOf(new[] { "Reliable", "Takedown" }));
        }

        [Test]
        public void UnitScopedWargearRule_FoldsOntoTheUnit_EvenOnATargetedUpgrade()
        {
            // Only WEAPON-scoped rules re-home. A targeted item granting Fear (unit-scoped) still buffs the
            // whole unit — scope, not the presence of a target, decides where a rule lives.
            RosterUnit roster = SniperDrones();
            roster.Sections[0].Options[0].ItemsGained[0].Rules = new List<SpecialRuleEntry>
            {
                new SpecialRuleEntry_Core("Fear"),
            };

            UnitFileEntry unit = CompileWithChoice(BookWith(roster), "drones", "controller-section", "controller");

            Assert.That(RuleNames(unit.SpecialRules), Does.Contain("Fear"));
            Assert.That(Weapon(unit, "Pulse Rifle").SpecialRules, Is.Empty);
        }

        [Test]
        public void UntargetedWargearWeaponRule_StillFoldsOntoTheUnit()
        {
            // AlienHives' "Toxic Cysts (Bane in Melee)" — a unit-wide upgrade with no target weapon. The
            // compiler leaves it at unit scope for army-load to spread.
            RosterUnit roster = SniperDrones();
            roster.Sections[0].Targets = new List<string>();
            roster.Sections[0].Options[0].ItemsGained[0].Rules = new List<SpecialRuleEntry>
            {
                new SpecialRuleEntry_Core("Bane in melee"),
            };

            UnitFileEntry unit = CompileWithChoice(BookWith(roster), "drones", "controller-section", "controller");

            Assert.That(RuleNames(unit.SpecialRules), Does.Contain("Bane in melee"));
            Assert.That(Weapon(unit, "Pulse Rifle").SpecialRules, Is.Empty);
            Assert.That(Weapon(unit, "Taser").SpecialRules, Is.Empty);
        }

        // ---- Army load: untargeted weapon rules spread across the unit's weapons ---------------------

        [Test]
        public void WeaponRuleNamedAtUnitLevel_AttachesToEveryWeapon_AndNotToTheUnit()
        {
            UnitData unit = LoadUnit(UnitEntry(modelCount: 2,
                unitRules: new SpecialRuleEntry[] { new SpecialRuleEntry_Core("Bane in melee") },
                WeaponEntry("Bio-Spiner", quantity: 2),
                WeaponEntry("Razor Claws", quantity: 2)));

            List<IWeapon> weapons = ((IUnit)unit).AllWeapons();
            Assert.That(weapons, Has.Count.EqualTo(4));

            foreach (IWeapon weapon in weapons)
            {
                Assert.That(weapon.RuleDefinitions.Select(r => r.Definition),
                    Has.One.SameAs(CoreRuleCatalog.BaneInMelee),
                    $"'{weapon.Name}' should carry the unit-granted weapon rule.");
            }

            Assert.That(unit.RuleDefinitions.Select(r => r.Definition),
                Has.None.SameAs(CoreRuleCatalog.BaneInMelee),
                "The rule lives on the weapons, not on the unit.");
        }

        [Test]
        public void UnitScopedRuleNamedAtUnitLevel_StaysOnTheUnit()
        {
            UnitData unit = LoadUnit(UnitEntry(modelCount: 1,
                unitRules: new SpecialRuleEntry[] { new SpecialRuleEntry_Core("Stealth") },
                WeaponEntry("Rifle", quantity: 1)));

            Assert.That(unit.RuleDefinitions.Select(r => r.Definition), Has.One.SameAs(CoreRuleCatalog.Stealth));
            Assert.That(((IUnit)unit).AllWeapons().Single().RuleDefinitions, Is.Empty);
        }

        [Test]
        public void WeaponRuleAtUnitLevel_OnAWeaponlessUnit_WarnsRatherThanAttachingNowhere()
        {
            List<string> warnings = new();
            void Capture(string message) => warnings.Add(message);

            RuleDiagnostics.OnWarning += Capture;
            try
            {
                UnitData unit = LoadUnit(UnitEntry(modelCount: 1,
                    unitRules: new SpecialRuleEntry[] { new SpecialRuleEntry_Core("Bane in melee") }));

                Assert.That(unit.RuleDefinitions.Select(r => r.Definition),
                    Has.None.SameAs(CoreRuleCatalog.BaneInMelee));
            }
            finally
            {
                RuleDiagnostics.OnWarning -= Capture;
            }

            Assert.That(warnings, Has.One.Contains("carries no weapons"),
                "A weapon rule with no weapon to land on must be reported, not swallowed.");
        }

        [Test]
        public void UnimplementedRuleNamedAtUnitLevel_IsStillSkippedWithAWarning()
        {
            List<string> warnings = new();
            void Capture(string message) => warnings.Add(message);

            RuleDiagnostics.OnWarning += Capture;
            try
            {
                UnitData unit = LoadUnit(UnitEntry(modelCount: 1,
                    unitRules: new SpecialRuleEntry[] { new SpecialRuleEntry_Core("Wolfborn") },
                    WeaponEntry("Rifle", quantity: 1)));

                Assert.That(((IUnit)unit).AllWeapons().Single().RuleDefinitions, Is.Empty,
                    "An unresolvable name must not be mistaken for a weapon rule and sprayed onto weapons.");
            }
            finally
            {
                RuleDiagnostics.OnWarning -= Capture;
            }

            Assert.That(warnings, Has.One.Contains("unimplemented special rule 'Wolfborn'"));
        }

        // ---- Helpers ---------------------------------------------------------------------------------

        private UnitData LoadUnit(UnitFileEntry entry)
        {
            var army = new ArmyListFile { Name = "Test", Units = { entry } };
            GameBootstrap.CreateArmy(_player, army, _store, _resolver);
            return _store.GetAllValues<UnitData>().Single();
        }

        private static IEnumerable<string> RuleNames(IEnumerable<SpecialRuleEntry> rules) =>
            rules.Select(r => r.PrintableName);

        private static WeaponFileEntry Weapon(UnitFileEntry unit, string name) =>
            unit.Weapons.Single(w => w.Name == name);

        private static UnitFileEntry CompileWithChoice(BookFile book, string unitId, string sectionId, string optionId)
        {
            var list = new BuilderList
            {
                Name = "Test", BookName = book.Name, PointsLimit = 500,
                Units =
                {
                    new BuilderUnit
                    {
                        RosterUnitId = unitId,
                        Choices = { new UpgradeChoice { SectionId = sectionId, OptionId = optionId, Count = 1 } },
                    },
                },
            };

            return ListCompiler.Compile(book, list).Units.Single();
        }

        private static BookFile BookWith(RosterUnit unit) => new()
        {
            Name = "Test Book", Faction = "Testers", Units = { unit },
        };

        /// <summary>DAO Union's Sniper Drones, trimmed to what this fixture exercises: a 30" Pulse Rifle and
        /// a melee Taser, three of each, with the "Upgrade all Pulse Rifles with: Drone Controller" section
        /// that grants Reliable + Takedown as a wargear rule-bundle.</summary>
        private static RosterUnit SniperDrones() => new()
        {
            Id = "drones", Name = "Sniper Drones",
            Quality = 5, Defense = 4,
            BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 75,
            Weapons =
            {
                WeaponEntry("Pulse Rifle", quantity: 3, rangeInches: 30),
                WeaponEntry("Taser", quantity: 3, rangeInches: 0),
            },
            Sections =
            {
                new UpgradeSection
                {
                    Id = "controller-section",
                    Label = "Upgrade all Pulse Rifles with",
                    Variant = UpgradeVariant.Upgrade,
                    Affects = UpgradeAffects.All,
                    Targets = { "Pulse Rifles" },
                    Options =
                    {
                        new UpgradeOption
                        {
                            Id = "controller", Label = "Drone Controller (Reliable, Takedown)", Cost = 75,
                            ItemsGained =
                            {
                                new ItemEntry
                                {
                                    Name = "Drone Controller", Quantity = 1,
                                    Rules =
                                    {
                                        new SpecialRuleEntry_Core("Reliable"),
                                        new SpecialRuleEntry_Core("Takedown"),
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        private static UnitFileEntry UnitEntry(int modelCount, SpecialRuleEntry[] unitRules,
            params WeaponFileEntry[] weapons) => new()
        {
            Name = "Testers",
            ModelCount = modelCount,
            Quality = 4,
            Defense = 4,
            SpecialRules = unitRules.ToList(),
            Weapons = weapons.ToList(),
        };

        private static WeaponFileEntry WeaponEntry(string name, int quantity, int rangeInches = 24) => new()
        {
            Name = name,
            Quantity = quantity,
            RangeInches = rangeInches,
            Attacks = 1,
            ArmorPenetration = 0,
        };
    }
}
