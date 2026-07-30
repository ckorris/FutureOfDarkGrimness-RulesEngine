using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 Sergeant - OPR 8HWdOwMYcI0p: "When this model attacks, unmodified results of 6 to hit deal 1
    // extra hit (only the original hit counts as a 6 for special rules)." A champion upgrade bought for
    // ONE model of a squad ("Upgrade up to three models with one: Sergeant"), whose 12 corpus sites were
    // dead because ListCompiler folded RulesGained unit-wide and the hit roll folds over the whole pool -
    // a ~10x over-grant the per-model wording forbids (owner ruling 2026-07-22).
    //
    // The scoping rides the aggregate format's only per-model identity: the WEAPON COPY. A weapon-scoped
    // rule gained from a targets-less per-model section becomes a CHAMPION MARK: after every section has
    // applied, one copy of each weapon profile takes the rule (the entry splits, #027's SameProfile keys
    // on rules so it never re-merges), and the rule never reaches unit.SpecialRules (army-load would
    // spread it across every copy). At load, round-robin hands the marked copies to a model; at fire
    // time the marked copy batches as its own volley and its rule fires on those dice alone - the same
    // mechanism that gives a joined hero's distinct weapon its own roll. The no-cascade parenthetical is
    // already engine law: 6-triggered rules read the unmodified rolls before synthetic hits insert.
    [TestFixture]
    public class SergeantRuleIntegrationTests
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

        // Surge's body under Sergeant's name, weapon-scoped - what the supplement authors.
        private static SpecialRuleDefinition SergeantDefinition() => new("Sergeant",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.UnmodifiedRollEquals(6),
                    new Effect.AddExtraHit(OnRollValue: 6),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "When this model attacks, unmodified results of 6 to hit deal 1 extra hit.");

        // ---- ListCompiler: the champion mark --------------------------------------------------------

        [Test]
        public void Sergeant_MarksOneCopyOfEachWeaponProfile_AndStaysOffTheUnit()
        {
            UnitFileEntry unit = Compile(DaemonWarriors(), Choice("champions", "sergeant"));

            foreach (string name in new[] { "Fireballs", "Hand Weapon" })
            {
                WeaponFileEntry marked = unit.Weapons.Single(w => w.Name == $"{name} (Sergeant)");
                Assert.That(marked.Quantity, Is.EqualTo(1), $"{name}: exactly one copy is the sergeant's");
                Assert.That(marked.SpecialRules.Single().PrintableName, Is.EqualTo("Sergeant"));
                Assert.That(unit.Weapons.Single(w => w.Name == name).Quantity, Is.EqualTo(4),
                    $"{name}: the other four models' copies are untouched");
            }

            Assert.That(unit.SpecialRules.Select(r => r.PrintableName), Does.Not.Contain("Sergeant"),
                "unit-level fold would spread the rule across every copy - the over-grant this exists to stop");

            // The rename is load-bearing, not cosmetic: the ranged-attack chooser keys its weapon pool by
            // NAME and faults on a duplicate ("An item with the same key has already been added"), found
            // in this slice's play probe. Every compiled name must stay unique.
            Assert.That(unit.Weapons.Select(w => w.Name), Is.Unique);
        }

        [Test]
        public void SergeantBoughtBeforeAReplace_MarksTheReplacementWeapon()
        {
            // The real books list the champion section FIRST and the "Replace all Hand Weapons" section
            // later. The mark must land on what the unit ENDS UP holding - a mark eaten by the replace
            // would be a paid upgrade silently lost.
            UnitFileEntry unit = Compile(DaemonWarriors(),
                Choice("champions", "sergeant"), Choice("swap", "axes"));

            Assert.That(unit.Weapons.Any(w => w.Name == "Hand Weapon"), Is.False, "the replace ran");

            WeaponFileEntry markedAxe = unit.Weapons.Single(w => w.Name == "Great Axe (Sergeant)");
            Assert.That(markedAxe.Quantity, Is.EqualTo(1),
                "the sergeant attacks with the axe he was handed, and the mark survived the swap");
        }

        [Test]
        public void TwoSergeantApplications_MarkTwoCopies()
        {
            // "Upgrade up to three models with one" - the section legally applies more than once.
            UnitFileEntry unit = Compile(DaemonWarriors(),
                new UpgradeChoice { SectionId = "champions", OptionId = "sergeant", Count = 2 });

            WeaponFileEntry marked = unit.Weapons.Single(w => w.Name == "Fireballs (Sergeant)");
            Assert.That(marked.Quantity, Is.EqualTo(2), "two champions, two marked copies");
        }

        [Test]
        public void AffectsAllSection_KeepsTheUnitWideFold()
        {
            // "Every model gets it" IS the whole pool: an affects-All grant of a weapon-scoped rule keeps
            // the existing unit-level fold (army-load spreads it to every copy), unchanged behaviour.
            RosterUnit roster = DaemonWarriors();
            roster.Sections.Single(s => s.Id == "champions").Affects = UpgradeAffects.All;

            UnitFileEntry unit = Compile(roster, Choice("champions", "sergeant"));

            Assert.That(unit.SpecialRules.Select(r => r.PrintableName), Does.Contain("Sergeant"));
            Assert.That(unit.Weapons.All(w => w.SpecialRules.Count == 0), Is.True,
                "no copy is singled out when the whole unit takes the rule");
        }

        [Test]
        public void UnitScopedChampionOption_StillFoldsOntoTheUnit()
        {
            // Banner rides the same section but resolves unit-scoped (a morale buff): the champion gate is
            // decided by the RULE's scope, not by the section's shape.
            UnitFileEntry unit = Compile(DaemonWarriors(), Choice("champions", "banner"));

            Assert.That(unit.SpecialRules.Select(r => r.PrintableName), Does.Contain("Banner (Hive Bond)"),
                "the unit-scoped option keeps the unit-level fold (Banner aliases Hive Bond)");
            Assert.That(unit.Weapons.All(w => w.SpecialRules.Count == 0), Is.True);
        }

        // ---- Army load: the marked copy is one model's weapon with a live rule ----------------------

        [Test]
        public void LoadedSergeantUnit_HasTheRuleOnExactlyOneCopyPerProfile_AndBatchesItAlone()
        {
            BuiltArmyFile army = CompileArmy(DaemonWarriors(), Choice("champions", "sergeant"));
            _resolver.RegisterOrReplace(SergeantDefinition());
            GameBootstrap.CreateArmy(_player, army, _store, _resolver);
            UnitData unit = _store.GetAllValues<UnitData>().Single();

            List<IWeapon> all = ((IUnit)unit).AllWeapons();
            Assert.That(all, Has.Count.EqualTo(10), "5 models x 2 weapons");
            Assert.That(all.Count(w => w.RuleDefinitions.Any(r => r.Definition.Name == "Sergeant")),
                Is.EqualTo(2), "one Fireballs copy and one Hand Weapon copy carry the live rule");
            Assert.That(unit.RuleDefinitions.Any(r => r.Definition.Name == "Sergeant"), Is.False);

            // The marked copy is its own hit batch: exactly one model rolls it, so the extra-hit fold
            // reads that model's dice alone - the joined-hero mechanism, reused.
            IWeapon marked = all.First(w => w.RuleDefinitions.Any(r => r.Definition.Name == "Sergeant"));
            Assert.That(marked.Name, Does.EndWith("(Sergeant)"),
                "the rename travels through load - the shoot chooser's name-keyed pool depends on it");
            Assert.That(HeroStatRules.LivingWeaponBatchOwners(unit, marked), Has.Count.EqualTo(1));
            IWeapon plain = all.First(w => w.Name == "Fireballs" && w.RuleDefinitions.Count == 0);
            Assert.That(HeroStatRules.LivingWeaponBatchOwners(unit, plain), Has.Count.EqualTo(4));
        }

        // ---- Helpers ---------------------------------------------------------------------------------

        private static UpgradeChoice Choice(string sectionId, string optionId) =>
            new UpgradeChoice { SectionId = sectionId, OptionId = optionId, Count = 1 };

        private UnitFileEntry Compile(RosterUnit roster, params UpgradeChoice[] choices) =>
            CompileArmy(roster, choices).Units.Single();

        private BuiltArmyFile CompileArmy(RosterUnit roster, params UpgradeChoice[] choices)
        {
            BookFile book = new() { Name = "Test Book", Faction = "Daemons", Units = { roster } };
            book.RuleDefinitions.Add(SergeantDefinition());

            BuilderUnit unit = new BuilderUnit { RosterUnitId = "warriors" };
            foreach (UpgradeChoice choice in choices) unit.Choices.Add(choice);

            return ListCompiler.Compile(book, new BuilderList
            {
                Name = "Test", BookName = book.Name, PointsLimit = 500, Units = { unit },
            });
        }

        /// <summary>The Wormhole Daemons shape: 5 models, two full weapon rows, the champion section
        /// FIRST (as in every real book) and a "Replace all Hand Weapons" section after it.</summary>
        private static RosterUnit DaemonWarriors() => new()
        {
            Id = "warriors", Name = "Daemon Warriors",
            Quality = 4, Defense = 4,
            BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
            Weapons =
            {
                new WeaponFileEntry { Name = "Fireballs", Quantity = 5, RangeInches = 12, Attacks = 1 },
                new WeaponFileEntry { Name = "Hand Weapon", Quantity = 5, RangeInches = 0, Attacks = 1 },
            },
            Sections =
            {
                new UpgradeSection
                {
                    Id = "champions",
                    Label = "Upgrade up to three models with one",
                    Variant = UpgradeVariant.Upgrade,
                    Affects = UpgradeAffects.Any,
                    MaxApplications = 3,
                    Options =
                    {
                        new UpgradeOption
                        {
                            Id = "sergeant", Label = "Sergeant", Cost = 5,
                            RulesGained = { new SpecialRuleEntry_Core("Sergeant") },
                        },
                        new UpgradeOption
                        {
                            Id = "banner", Label = "Banner", Cost = 5,
                            RulesGained =
                            {
                                new SpecialRuleEntry_Alias("Banner", new SpecialRuleEntry_Core("Hive Bond")),
                            },
                        },
                    },
                },
                new UpgradeSection
                {
                    Id = "swap",
                    Label = "Replace all Hand Weapons",
                    Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.All,
                    Targets = { "Hand Weapons" },
                    Options =
                    {
                        new UpgradeOption
                        {
                            Id = "axes", Label = "Great Axes", Cost = 10,
                            WeaponsGained =
                            {
                                new WeaponFileEntry { Name = "Great Axe", Quantity = 1, RangeInches = 0, Attacks = 2 },
                            },
                        },
                    },
                },
            },
        };
    }
}
