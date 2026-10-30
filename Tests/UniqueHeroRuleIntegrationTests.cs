using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.GameModel;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #430 - the 3.6 book rule Unique Hero: "Counts as having Hero, and this model may only be taken once per
    // army." The "counts as having Hero" half is delivered by granting the real Hero rule at army load
    // (CoreRuleCatalog.ImpliedRuleName -> GameBootstrap.AttachRulesFromArmyList), so these tests drive the
    // real army-load path (GameBootstrap.CreateArmy) and then ask the engine's own Hero reads - the #006 join,
    // the transport ride cap - rather than looking for a name. The once-per-army half is list-build time and
    // lives in ListValidatorTests / ForceOrgValidatorTests.
    [TestFixture]
    public class UniqueHeroRuleIntegrationTests
    {
        private static readonly PlayerID Player = new PlayerID(Guid.NewGuid());

        [Test]
        public void UniqueHero_Catalog_CarriesTheThreePointSixText()
        {
            Assert.That(CoreRuleCatalog.All, Does.Contain(CoreRuleCatalog.UniqueHero));
            Assert.That(CoreRuleCatalog.UniqueHero.Description,
                Is.EqualTo("Counts as having Hero, and this model may only be taken once per army."));
            Assert.That(CoreRuleCatalog.ImpliedRuleName("Unique Hero"), Is.EqualTo("Hero"));
            Assert.That(CoreRuleCatalog.ImpliedRuleName("Hero"), Is.Null);
            Assert.That(CoreRuleCatalog.ImpliedRuleName("Tough"), Is.Null);
        }

        [Test]
        public void UniqueHero_JoinsAMultiModelUnit_LikeAHero()
        {
            ArmyListFile army = ArmyWith(
                Squad("squad-1"),
                UniqueHero("Drekhor", joins: "squad-1", tough: 3));

            IReadOnlyList<UnitData> units = Create(army);

            UnitData host = units.Single();
            Assert.That(host.HasHero, Is.True, "the Unique Hero joined the squad - no separate unit deploys");
            Assert.That(host.HeroAttachment!.Name, Is.EqualTo("Drekhor"));
            Assert.That(host.Models.Count, Is.EqualTo(6), "five squad models plus the hero's");
            Assert.That(host.HeroAttachment.HeroWounds, Is.EqualTo(3), "the join reads the hero's own Tough");
        }

        [Test]
        public void UniqueHero_OverToughSix_DoesNotJoin_SameCapAsAHero()
        {
            ArmyListFile army = ArmyWith(
                Squad("squad-1"),
                UniqueHero("Titan", joins: "squad-1", tough: 7));

            IReadOnlyList<UnitData> units = Create(army);

            Assert.That(units, Has.Count.EqualTo(2), "Tough(7) is over the join cap, so the hero deploys solo");
            Assert.That(units.Any(u => u.HasHero), Is.False);
        }

        [Test]
        public void UniqueHero_CannotJoinAUnitThatAlreadyHasAHero()
        {
            UnitFileEntry squad = Squad("squad-1");
            squad.SpecialRules.Add(new SpecialRuleEntry_Core("Hero"));
            ArmyListFile army = ArmyWith(squad, UniqueHero("Drekhor", joins: "squad-1", tough: 3));

            Assert.That(Create(army), Has.Count.EqualTo(2), "a host that is itself a Hero is ineligible");
        }

        [Test]
        public void UniqueHero_HoldsExactlyOneHeroRule_EvenWhenTheEntryNamesHeroToo()
        {
            UnitFileEntry both = UniqueHero("Drekhor", joins: null, tough: 3);
            both.SpecialRules.Add(new SpecialRuleEntry_Core("Hero"));

            UnitData unit = Create(ArmyWith(both)).Single();

            Assert.That(unit.RuleDefinitions.Count(r => r.Definition == CoreRuleCatalog.Hero), Is.EqualTo(1));
            Assert.That(unit.RuleDefinitions.Count(r => r.Definition == CoreRuleCatalog.UniqueHero), Is.EqualTo(1));
        }

        [Test]
        public void UniqueHero_GrantsHero_ToTheBearerOnly()
        {
            IReadOnlyList<UnitData> units = Create(ArmyWith(Squad("squad-1"), UniqueHero("Drekhor", joins: null, tough: 3)));

            UnitData squad = units.Single(u => u.Name == "Squad");
            UnitData hero = units.Single(u => u.Name == "Drekhor");
            Assert.That(squad.RuleDefinitions.Any(r => r.Definition == CoreRuleCatalog.Hero), Is.False);
            Assert.That(hero.RuleDefinitions.Any(r => r.Definition == CoreRuleCatalog.Hero), Is.True);
        }

        [Test]
        public void SoloUniqueHero_UsesTheHeroTransportRules()
        {
            // TransportUtilities reads Hero off the unit's attached rules for a solo model: a Hero takes one
            // space however Tough it is (up to its Tough(6) ride cap), where a plain Tough(6) model takes three.
            UnitFileEntry uniqueHero = UniqueHero("Drekhor", joins: null, tough: 6);
            UnitFileEntry nonHero = new UnitFileEntry { Name = "Brute", ModelCount = 1, Quality = 4, Defense = 4 };
            nonHero.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric("Tough", 6));

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameBootstrap.CreateArmy(Player, ArmyWith(uniqueHero, nonHero), store, CoreRuleCatalog.CreateResolver());
            List<UnitData> units = store.GetAllValues<UnitData>().ToList();
            foreach (UnitData unit in units)
            {
                UnitCreationRules.Apply(unit, new RuleEvaluator(new FixedDiceRoller(4)));
            }

            UnitData hero = units.Single(u => u.Name == "Drekhor");
            UnitData brute = units.Single(u => u.Name == "Brute");
            Assert.That(hero.Models[0].TotalWounds, Is.EqualTo(6f));
            Assert.That(TransportUtilities.GetUnitSpaceCost(hero), Is.EqualTo(1), "a Hero takes one space");
            Assert.That(TransportUtilities.GetUnitSpaceCost(brute), Is.EqualTo(3), "a non-Hero Tough(6) model takes three");
        }

        // ---- Fixtures --------------------------------------------------------------------------------

        private static UnitFileEntry Squad(string id) =>
            new UnitFileEntry { Id = id, Name = "Squad", ModelCount = 5, Quality = 4, Defense = 4, PointCost = 100 };

        private static UnitFileEntry UniqueHero(string name, string? joins, int tough)
        {
            UnitFileEntry hero = new UnitFileEntry
            {
                Name = name, ModelCount = 1, Quality = 3, Defense = 3, PointCost = 80, JoinsUnitId = joins,
            };
            hero.SpecialRules.Add(new SpecialRuleEntry_Core("Unique Hero"));
            hero.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric("Tough", tough));
            return hero;
        }

        private static ArmyListFile ArmyWith(params UnitFileEntry[] units) =>
            new ArmyListFile { Name = "Test", PointsLimit = 1000, Units = units.ToList() };

        private static IReadOnlyList<UnitData> Create(ArmyListFile army)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameBootstrap.CreateArmy(Player, army, store, CoreRuleCatalog.CreateResolver());
            return store.GetAllValues<UnitData>().ToList();
        }
    }
}
