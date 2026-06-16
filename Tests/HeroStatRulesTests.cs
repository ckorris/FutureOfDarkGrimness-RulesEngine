using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // Red tests pinning the #006 stat-divergence slices C/D/E. HeroStatRules is currently stubbed to the
    // pre-#006 behavior (always the unit's own stat), so these FAIL until the matching slice fills the
    // helper in (and wires the stage to call it). Each is [Ignore]d so the committed suite stays green
    // ("never commit red"); remove the [Ignore] when implementing that slice.
    [TestFixture]
    public class HeroStatRulesTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        [Ignore("#006 slice C (morale on behalf) — not yet implemented")]
        public void GetMoraleQuality_UsesHerosQuality_WhileHeroLives()
        {
            (UnitData host, _, _) = MakeMergedUnit(gruntCount: 3); // host Quality 4, hero Quality 2

            Assert.That(HeroStatRules.GetMoraleQuality(host), Is.EqualTo(2),
                "a unit with a living hero tests morale at the hero's Quality.");
        }

        [Test]
        [Ignore("#006 slice D (last-model Defense) — not yet implemented")]
        public void GetSaveDefense_UsesHerosDefense_WhenHeroIsSoleSurvivor()
        {
            (UnitData host, _, List<ModelData> grunts) = MakeMergedUnit(gruntCount: 2); // host Defense 4, hero Defense 2

            foreach (ModelData grunt in grunts)
                grunt.DealWounds(grunt.TotalWounds); // kill the rank and file; only the hero remains

            Assert.That(HeroStatRules.GetSaveDefense(host), Is.EqualTo(2),
                "once the hero is the only living model, the unit saves at the hero's Defense.");
        }

        [Test]
        [Ignore("#006 slice D (last-model Defense) — not yet implemented")]
        public void GetSaveDefense_UsesUnitDefense_WhileRankAndFileLive()
        {
            (UnitData host, _, _) = MakeMergedUnit(gruntCount: 2);

            Assert.That(HeroStatRules.GetSaveDefense(host), Is.EqualTo(4),
                "while any non-hero model lives, the hero saves at the unit's Defense.");
        }

        [Test]
        [Ignore("#006 slice E (per-model attack Quality) — not yet implemented")]
        public void GetAttackQuality_UsesHerosQuality_ForAHeroOnlyWeaponBatch()
        {
            (UnitData host, ModelData hero, _) = MakeMergedUnit(gruntCount: 3);
            Weapon heroWeapon = new Weapon("Hero Blade", 0f, 2, 0);
            hero.Weapons.Add(heroWeapon);

            Assert.That(HeroStatRules.GetAttackQuality(host, heroWeapon), Is.EqualTo(2),
                "a weapon batch owned only by the hero fires at the hero's Quality.");
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
