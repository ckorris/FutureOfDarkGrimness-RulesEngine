using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #006 slice F (per-model combat-rule carriage): proves a joined
    // hero's OWN unit-scoped hit rule (Relentless) is relocated onto the hero MODEL at merge and then fires
    // for the hero's weapon batch ONLY — through the REAL RollToHitStage and the model-aware RuleEvaluator
    // dispatch. FixedDiceRoller(6) makes every die a natural 6, so each case reads as "2 hits when the
    // hero's rule fires, 1 when it doesn't." Mirrors ExtraHitRuleIntegrationTests' RollToHitStage harness.
    [TestFixture]
    public class HeroPerModelRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position FarPos = new Position(20, 5); // > 9" base-to-base (Relentless gate)

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6)); // every die shows 6
        }

        [Test]
        public async Task HeroRule_FiresForHeroWeaponBatch()
        {
            Weapon gruntWeapon = new Weapon("Rifle", 48f, 1, 0);
            Weapon heroWeapon = new Weapon("Hero Rifle", 48f, 1, 0);
            DataBinding<UnitData> host = MakeMergedHeroUnit(gruntWeapon, heroWeapon);

            RollToHitResults result = await RunStage(host, MakeTarget(FarPos), heroWeapon);

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "the hero's Relentless (moved onto the hero model at merge) fires for the hero's own batch.");
        }

        [Test]
        public async Task HeroRule_DoesNotFireForRankAndFileBatch()
        {
            Weapon gruntWeapon = new Weapon("Rifle", 48f, 1, 0);
            Weapon heroWeapon = new Weapon("Hero Rifle", 48f, 1, 0);
            DataBinding<UnitData> host = MakeMergedHeroUnit(gruntWeapon, heroWeapon);

            RollToHitResults result = await RunStage(host, MakeTarget(FarPos), gruntWeapon);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "Relentless is the hero's per-model rule — the rank-and-file weapon batch doesn't get it.");
        }

        [Test]
        public async Task HeroRule_GoneWhenHeroDead()
        {
            Weapon gruntWeapon = new Weapon("Rifle", 48f, 1, 0);
            Weapon heroWeapon = new Weapon("Hero Rifle", 48f, 1, 0);
            DataBinding<UnitData> host = MakeMergedHeroUnit(gruntWeapon, heroWeapon);

            ModelID heroId = host.GetValue().HeroAttachment!.HeroModelId;
            IModel hero = host.GetValue().Models.First(model => model.ID == heroId);
            hero.DealWounds(hero.TotalWounds); // the hero falls — its batch has no living owner

            RollToHitResults result = await RunStage(host, MakeTarget(FarPos), heroWeapon);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "a dead hero contributes no per-model rules.");
        }

        [Test]
        public async Task HeroRule_SuppressedWhenWeaponSharedWithRankAndFile()
        {
            // Deferred same-weapon-pooling collision: if a grunt carries the SAME weapon as the hero, the
            // batch is no longer hero-only, so the hero's per-model rule does not apply — matching the
            // slice-E attack-Quality collision handling (unit behavior stands).
            Weapon shared = new Weapon("Rifle", 48f, 1, 0);
            DataBinding<UnitData> host = MakeMergedHeroUnit(shared, shared);

            RollToHitResults result = await RunStage(host, MakeTarget(FarPos), shared);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "a weapon the rank and file also carry isn't a hero-only batch — the hero rule is suppressed.");
        }

        // --- harness (mirrors ExtraHitRuleIntegrationTests) ---

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, Weapon weaponType, bool isMelee = false, bool isCharging = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var metadata = new CombatMetadata(_ctx, attacker, defender, weaponType, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: isCharging);
            metadata.AddResult(new DetermineHitRollNeededResults(4)); // a 6 clears a 4+ threshold

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private static float TotalHits(RollToHitResults results) =>
            results.SuccessfulHitList.Sum(hit => hit.HitCount);

        // Builds a host (grunts carrying gruntWeapon) merged with a 1-model hero carrying heroWeapon plus
        // Relentless as a UNIT rule — which the merge should relocate onto the hero MODEL.
        private DataBinding<UnitData> MakeMergedHeroUnit(Weapon gruntWeapon, Weapon heroWeapon)
        {
            UnitData host = MakeUnit(quality: 4, defense: 4, weapon: gruntWeapon, modelCount: 3);
            UnitData hero = MakeUnit(quality: 2, defense: 2, weapon: heroWeapon, modelCount: 1);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.AttachRuleDefinition(new ResolvedRule("Relentless", CoreRuleCatalog.Relentless));

            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            return _store.GetDataBinding<UnitData>(_store.Create(host));
        }

        private UnitData MakeUnit(int quality, int defense, Weapon weapon, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon> { weapon }, AttackerPos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: quality, defense: defense, modelBindings: modelBindings);
        }

        private DataBinding<UnitData> MakeTarget(Position position)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), position, _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Target",
                quality: 4, defense: 4, modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
