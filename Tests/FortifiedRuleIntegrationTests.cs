using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Fortified reduces the incoming WEAPON AP by 1, floored at AP(0): unlike Shielded's flat +1 defense,
    // it only cancels existing penetration and does nothing against an AP(0) hit. Proves the primitive
    // flows end to end — the defender (Subject) fires Effect.ReduceArmorPenetration at RollToHitStage
    // (summed into RollToHitResults.ArmorPenetrationReduction), and DetermineSaveRollsNeededStage clamps
    // the weapon AP by it. Defense 4 throughout; FixedDiceRoller(6) lands the hit.
    [TestFixture]
    public class FortifiedRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(20, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6));
        }

        [Test]
        public async Task FortifiedDefender_VsAp2_ReducesThresholdByOne()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            AttachFortified(defender);

            int saveNeeded = await RunSavePipeline(attacker, defender, weaponAp: 2);

            Assert.That(saveNeeded, Is.EqualTo(5),
                "defense 4 + AP(2-1=1) = 5: Fortified cancels one point of the weapon's AP.");
        }

        [Test]
        public async Task FortifiedDefender_VsAp0_DoesNothing_FloorHolds()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            AttachFortified(defender);

            int saveNeeded = await RunSavePipeline(attacker, defender, weaponAp: 0);

            Assert.That(saveNeeded, Is.EqualTo(4),
                "AP(0) floored stays 0 — Fortified does nothing against an AP(0) hit (unlike a flat +1 save).");
        }

        [Test]
        public async Task NoFortified_VsAp2_FullPenetration()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);

            int saveNeeded = await RunSavePipeline(attacker, defender, weaponAp: 2);

            Assert.That(saveNeeded, Is.EqualTo(6), "defense 4 + full AP 2 = 6, no reduction.");
        }

        private async Task<int> RunSavePipeline(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, int weaponAp)
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: weaponAp);

            var hitStage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            hitStage.NextStage.Bind("done");
            var hitMeta = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            hitMeta.AddResult(new DetermineHitRollResults(4, attackCount: 1)); // a 6 clears 4+
            await hitStage.Enter(hitMeta);
            hitMeta.QueryForResult(out RollToHitResults hits);

            var saveStage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            saveStage.NextStage.Bind("done");
            var saveMeta = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            saveMeta.AddResult(hits);
            saveMeta.AddResult(new CoverCheckResults(0)); // no cover
            await saveStage.Enter(saveMeta);
            saveMeta.QueryForResult(out DetermineSaveRollNeededResults saves);

            return saves.PendingSaveRollsList[0].SaveNeeded;
        }

        private static void AttachFortified(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Fortified", CoreRuleCatalog.Fortified));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: position, gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
