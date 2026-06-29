using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Regeneration's wound-ignore — and its
    // SUPPRESSION by Unstoppable — flow through the REAL AssignWoundsStage. The stage fires the
    // Shooting_OnSaveRollComplete "when" for BOTH participants, the RuleEvaluator runs the
    // suppression first-pass, the WoundIgnoreSink folds the surviving IgnoreWound op, and the stage
    // rolls one d6 per wound and drops the ignored count — none of it interpreted by the stage.
    //
    // Uses a ProbabilisticDiceRoller so the per-wound roll is deterministic: rolling N dice yields
    // N/6 per face, so Regeneration(5+) ignores exactly N/3 wounds (faces 5 and 6). Defender has
    // 5 wounds so the count stays sub-lethal and lands in the "ask the player" branch, where the
    // capturing requester records the requested wound count (the cleanest observable).
    [TestFixture]
    public class WoundIgnoreRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private CapturingWoundRequester _requester = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new CapturingWoundRequester();
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller());
        }

        [Test]
        public async Task RegenerationDefender_IgnoresAThirdOfWounds()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachRule(defender, "Regeneration", CoreRuleCatalog.Regeneration);

            await RunStage(attacker, defender, failedSaves: 3);

            // 3 failed saves; Regeneration rolls 3 d6 and ignores the 5+s: 2 faces × 3/6 = 1.0 ignored.
            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f).Within(0.0001f),
                "Regeneration ignores a third of the wounds (5+ on a probabilistic d6).");
        }

        [Test]
        public async Task UnstoppableAttacker_SuppressesRegeneration_FullWoundsLand()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRule(attacker, "Unstoppable", CoreRuleCatalog.Unstoppable);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachRule(defender, "Regeneration", CoreRuleCatalog.Regeneration);

            await RunStage(attacker, defender, failedSaves: 3);

            // Unstoppable's IgnoreRule("Regeneration") is consumed by the suppression first-pass before
            // the ignore sink folds, so nothing is ignored — all three wounds land.
            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f).Within(0.0001f),
                "Unstoppable suppresses Regeneration end-to-end; the full wound count is assigned.");
        }

        // #093 combat-kind threaded into the save context: "Unstoppable when shooting" suppresses
        // Regeneration on a shooting attack (Not(IsMelee) holds), so all wounds land.
        [Test]
        public async Task UnstoppableWhenShootingAttacker_Shooting_SuppressesRegeneration()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRule(attacker, "Unstoppable when shooting", CoreRuleCatalog.UnstoppableWhenShooting);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachRule(defender, "Regeneration", CoreRuleCatalog.Regeneration);

            await RunStage(attacker, defender, failedSaves: 3); // shooting

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f).Within(0.0001f),
                "shooting: the gate holds, Regeneration is suppressed, all three wounds land.");
        }

        // In melee the gate (Not(IsMelee)) fails, so the suppression does NOT fire and Regeneration ignores
        // a third — proving IsMelee now reaches the save-complete evaluation.
        [Test]
        public async Task UnstoppableWhenShootingAttacker_Melee_DoesNotSuppress()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRule(attacker, "Unstoppable when shooting", CoreRuleCatalog.UnstoppableWhenShooting);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachRule(defender, "Regeneration", CoreRuleCatalog.Regeneration);

            await RunStage(attacker, defender, failedSaves: 3, isMelee: true);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f).Within(0.0001f),
                "melee: the gate fails, Regeneration is not suppressed and ignores a third (3 → 2).");
        }

        // Resistance is Regeneration's wound-ignore at a higher threshold (6+ vs 5+) — ignores only the
        // 6s. With 3 failed saves: 1 face × 3/6 = 0.5 ignored, so 2.5 land (vs Regeneration's 2.0). Locks
        // in the MinRoll: 6. Protected shares this exact definition.
        [Test]
        public async Task ResistanceDefender_IgnoresWoundsOnSixOnly()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachRule(defender, "Resistance", CoreRuleCatalog.Resistance);

            await RunStage(attacker, defender, failedSaves: 3);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2.5f).Within(0.0001f),
                "Resistance ignores a wound on 6+ only (a sixth), so 3 → 2.5.");
        }

        private static void AttachRule(DataBinding<UnitData> unit, string name, SpecialRuleDefinition definition)
            => unit.GetValue().AttachRuleDefinition(
                new ResolvedRule(name, definition, System.Array.Empty<RuleArgument>()));

        private async Task RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            int failedSaves, bool isMelee = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee);

            // One FailedSaveInfo per wound (SaveCount == its dice TotalRolls == 1).
            var failedList = new List<FailedSaveInfo>();
            for (int i = 0; i < failedSaves; i++)
            {
                failedList.Add(new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)));
            }
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failedList));

            await stage.Enter(metadata);
        }

        private DataBinding<UnitData> MakeUnit(int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
