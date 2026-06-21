using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Rending's AP promotion flows from the REAL
    // RollToHitStage to the REAL DetermineSaveRollsNeededStage. On an unmodified 6 to hit, Rending
    // fires Shooting_OnHitRollComplete -> RollModifier(Save, -4); RollToHitStage folds it into
    // RollToHitResults.SaveModifier (where the unmodified roll is still correct), and the save stage
    // raises the defender's save threshold by 4. Modelled as a flat save modifier (queue-test model);
    // true per-hit AP is deferred. FixedDiceRoller(6) makes every hit a natural 6.
    [TestFixture]
    public class RendingRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(20, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6)); // every die shows 6
        }

        [Test]
        public async Task RendingAttacker_OnNatural6_RaisesSaveThresholdByFour()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos); // defense 4
            AttachRending(attacker);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(-4), "Rending queues a -4 save modifier on a natural 6 to hit.");

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(8),
                    "base defense 4 + 4 from Rending's AP = save threshold 8 (pre-clamp).");
            }
        }

        [Test]
        public async Task NoRending_SaveThresholdUnchanged()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(0), "no Rending → no carried save modifier.");

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(4), "just the defender's defense, no AP.");
            }
        }

        private async Task<RollToHitResults> RunHitStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var metadata = NewMetadata(attacker, defender);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1)); // a 6 clears 4+
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private async Task<DetermineSaveRollNeededResults> RunSaveNeededStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, RollToHitResults hits)
        {
            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var metadata = NewMetadata(attacker, defender);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0)); // no cover
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result;
        }

        private CombatMetadata NewMetadata(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            return new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
        }

        private static void AttachRending(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
