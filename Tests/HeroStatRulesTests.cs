using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Pins the #006 stat-divergence slices C/D/E: morale at the hero's Quality while it lives, saves at
    // the hero's Defense once it's the sole survivor, and hero-only weapon batches hitting at the hero's
    // Quality. HeroStatRules is fully implemented and wired into the resolution stages; these tests keep
    // that behavior from regressing.
    [TestFixture]
    public class HeroStatRulesTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void GetMoraleQuality_UsesHerosQuality_WhileHeroLives()
        {
            (UnitData host, _, _) = MakeMergedUnit(gruntCount: 3); // host Quality 4, hero Quality 2

            Assert.That(HeroStatRules.GetMoraleQuality(host), Is.EqualTo(2),
                "a unit with a living hero tests morale at the hero's Quality.");
        }

        [Test]
        public void GetMoraleQuality_RevertsToUnitQuality_WhenHeroIsDead()
        {
            (UnitData host, ModelData hero, _) = MakeMergedUnit(gruntCount: 3);
            hero.DealWounds(hero.TotalWounds); // the hero falls

            Assert.That(HeroStatRules.GetMoraleQuality(host), Is.EqualTo(4),
                "with the hero dead, the unit tests morale at its own Quality again.");
        }

        [Test]
        public void GetMoraleQuality_UsesUnitQuality_WithNoHero()
        {
            UnitData plain = MakeUnit(modelCount: 3, quality: 4, defense: 4);

            Assert.That(HeroStatRules.GetMoraleQuality(plain), Is.EqualTo(4),
                "a unit with no hero is unaffected.");
        }

        // #421 - OPR 3.6 Hero: "The Hero's unit ... may use the Hero's Quality for Morale Tests as long as the
        // Hero isn't Shaken." Shaken is a unit token, so a joined hero is Shaken exactly when its unit is.
        [Test]
        public void GetMoraleQuality_UsesUnitQuality_WhileTheJoinedHeroIsShaken()
        {
            (UnitData host, _, _) = MakeMergedUnit(gruntCount: 3); // host Quality 4, hero Quality 2
            host.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            Assert.That(HeroStatRules.GetMoraleQuality(host), Is.EqualTo(4),
                "a Shaken hero lends nothing - the unit's own Quality stands.");
        }

        [Test]
        public void GetMoraleQuality_UsesHerosQuality_AgainOnceTheShakenTokenClears()
        {
            (UnitData host, _, _) = MakeMergedUnit(gruntCount: 3);
            host.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            host.Tokens.RemoveTokens(TokenType.Shaken); // recovered by an idle activation

            Assert.That(HeroStatRules.GetMoraleQuality(host), Is.EqualTo(2),
                "an un-Shaken hero lends its Quality again.");
        }

        [Test]
        public void ShakenIsAUnitToken_SoTheJoinedHeroIsShakenExactlyWhenItsUnitIs()
        {
            (UnitData host, ModelData hero, _) = MakeMergedUnit(gruntCount: 3);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(host));

            MoraleUtilities.ApplyShaken(binding); // the failed-morale path's own token write

            Assert.That(host.Tokens.HasToken(TokenType.Shaken), Is.True, "the token lands on the unit");
            Assert.That(hero.Tokens.HasToken(TokenType.Shaken), Is.False, "never on the hero's model");
            Assert.That(HeroStatRules.GetMoraleQuality(host), Is.EqualTo(4),
                "so 'the Hero isn't Shaken' reads the unit's token");
        }

        [Test]
        public void GetSaveDefense_UsesHerosDefense_WhenHeroIsSoleSurvivor()
        {
            (UnitData host, _, List<ModelData> grunts) = MakeMergedUnit(gruntCount: 2); // host Defense 4, hero Defense 2

            foreach (ModelData grunt in grunts)
                grunt.DealWounds(grunt.TotalWounds); // kill the rank and file; only the hero remains

            Assert.That(HeroStatRules.GetSaveDefense(host), Is.EqualTo(2),
                "once the hero is the only living model, the unit saves at the hero's Defense.");
        }

        [Test]
        public void GetSaveDefense_UsesUnitDefense_WhileRankAndFileLive()
        {
            (UnitData host, _, _) = MakeMergedUnit(gruntCount: 2);

            Assert.That(HeroStatRules.GetSaveDefense(host), Is.EqualTo(4),
                "while any non-hero model lives, the hero saves at the unit's Defense.");
        }

        [Test]
        public void GetAttackQuality_UsesHerosQuality_ForAHeroOnlyWeaponBatch()
        {
            (UnitData host, ModelData hero, _) = MakeMergedUnit(gruntCount: 3);
            Weapon heroWeapon = new Weapon("Hero Blade", 0f, 2, 0);
            hero.Weapons.Add(heroWeapon);

            Assert.That(HeroStatRules.GetAttackQuality(host, heroWeapon), Is.EqualTo(2),
                "a weapon batch owned only by the hero fires at the hero's Quality.");
        }

        [Test]
        public void GetAttackQuality_StaysUnitQuality_WhenARankAndFileShareTheWeapon()
        {
            (UnitData host, ModelData hero, List<ModelData> grunts) = MakeMergedUnit(gruntCount: 3);
            // Same stats AND no rules => WeaponComparer pools them, so the batch isn't hero-only (the
            // deferred same-weapon-collision case): it must fall back to the unit's Quality.
            hero.Weapons.Add(new Weapon("Rifle", 24f, 1, 0));
            grunts[0].Weapons.Add(new Weapon("Rifle", 24f, 1, 0));

            Assert.That(HeroStatRules.GetAttackQuality(host, hero.Weapons.Last()), Is.EqualTo(4),
                "a weapon the rank and file also carry is not a hero-only batch — unit Quality stands.");
        }

        // --- helpers ---

        private (UnitData Host, ModelData Hero, List<ModelData> Grunts) MakeMergedUnit(int gruntCount)
        {
            UnitData host = MakeUnit(gruntCount, quality: 4, defense: 4);
            UnitData hero = MakeUnit(1, quality: 2, defense: 2);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));

            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            ModelID heroId = host.HeroAttachment!.HeroModelId;
            ModelData heroModel = (ModelData)host.Models.First(m => m.ID == heroId);
            List<ModelData> grunts = host.Models.Where(m => m.ID != heroId).Cast<ModelData>().ToList();
            return (host, heroModel, grunts);
        }

        private UnitData MakeUnit(int modelCount, int quality, int defense)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), new Position(0, 0), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: quality, defense: defense, modelBindings: modelBindings);
        }
    }
}
