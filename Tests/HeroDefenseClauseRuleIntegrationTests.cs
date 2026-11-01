using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #421 - OPR 3.6 Hero: "Heroes with up to Tough(6) may deploy as part of one multi-model unit without another Hero,
    // but must use the unit's Defense until all other models have been killed." Unchanged from 3.5.1 and already what
    // #006 built (HeroStatRules.GetSaveDefense); HeroStatRulesTests pins the seam, and this pins it through the REAL
    // shooting save pipeline (RollToHitStage -> DetermineSaveRollsNeededStage), which every attack kind shares.
    //
    // The rig mirrors FortifiedRuleIntegrationTests: a 6 lands the hit, AP(0), no cover. Host Defense 5 with three
    // rank-and-file models; the joined hero's own Defense is 2.
    [TestFixture]
    public class HeroDefenseClauseRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6));
        }

        [Test]
        public async Task WhileOtherModelsLive_TheUnitSavesAtTheUnitsDefense()
        {
            (DataBinding<UnitData> unit, List<IModel> rankAndFile) = MergedUnit();
            rankAndFile[0].DealWounds(rankAndFile[0].TotalWounds);
            rankAndFile[1].DealWounds(rankAndFile[1].TotalWounds); // one squadmate still standing

            Assert.That(await SaveNeeded(unit), Is.EqualTo(5),
                "'must use the unit's Defense until ALL other models have been killed' - one is still alive");
        }

        [Test]
        public async Task OnceEveryOtherModelIsDead_TheHeroSavesAtItsOwnDefense()
        {
            (DataBinding<UnitData> unit, List<IModel> rankAndFile) = MergedUnit();
            foreach (IModel model in rankAndFile) model.DealWounds(model.TotalWounds);

            Assert.That(await SaveNeeded(unit), Is.EqualTo(2), "the hero alone: its own Defense 2+");
        }

        // --- helpers -----------------------------------------------------------------------------------------------

        private async Task<int> SaveNeeded(DataBinding<UnitData> defender)
        {
            DataBinding<UnitData> attacker = Register(Unit("Shooters", models: 1, defense: 4, x: 0f));
            var weapon = new Weapon("Rifle", rangeInches: 48f, attacks: 1, armorPenetration: 0);

            var hitStage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            hitStage.NextStage.Bind("done");
            var hitMeta = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            hitMeta.AddResult(new DetermineHitRollResults(4, attackCount: 1));
            await hitStage.Enter(hitMeta);
            hitMeta.QueryForResult(out RollToHitResults hits);

            var saveStage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            saveStage.NextStage.Bind("done");
            var saveMeta = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            saveMeta.AddResult(hits);
            saveMeta.AddResult(new CoverCheckResults(0));
            await saveStage.Enter(saveMeta);
            saveMeta.QueryForResult(out DetermineSaveRollNeededResults saves);

            return saves.PendingSaveRollsList[0].SaveNeeded;
        }

        private (DataBinding<UnitData> Unit, List<IModel> RankAndFile) MergedUnit()
        {
            UnitData host = Unit("Squad", models: 3, defense: 5, x: 20f);
            UnitData hero = Unit("Captain", models: 1, defense: 2, x: 20f, z: 6.5f);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            ModelID heroId = host.HeroAttachment!.HeroModelId;
            return (Register(host), host.Models.Where(m => m.ID != heroId).ToList());
        }

        private UnitData Unit(string name, int models, int defense, float x, float z = 5f)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), new Position(x, z + i * 2f), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            return new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: defense,
                modelBindings: bindings);
        }

        private DataBinding<UnitData> Register(UnitData unit) =>
            _store.GetDataBinding<UnitData>(_store.Create(unit));
    }
}
