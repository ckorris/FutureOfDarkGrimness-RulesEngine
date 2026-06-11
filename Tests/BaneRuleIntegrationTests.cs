using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Bane's save re-roll flows through the REAL
    // AssignWoundsStage. Bane fires Shooting_OnSaveRollComplete -> ApplyReroll(Save, OnUnmodifiedValue);
    // the RerollSink folds it, and the stage re-rolls the defender's natural-6 saves, adding the new
    // failures to the wound count BEFORE Deadly multiplies. ProbabilisticDiceRoller makes the re-roll
    // deterministic: re-rolling N saved 6s at save-needed 4 yields N × P(below 4) = N × 3/6 new wounds.
    // Reuses WoundTestContext / CapturingWoundRequester from WoundRuleIntegrationTests.
    [TestFixture]
    public class BaneRuleIntegrationTests
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
        public async Task BaneAttacker_RerollsSavedSixesIntoExtraWounds()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachBane(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender);

            // 1 original failed save + re-rolling 2 saved 6s (each fails below 4 → 2 × 3/6 = 1.0) = 2.
            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f).Within(0.0001f),
                "Bane re-rolls the two saved 6s, adding one expected failure to the original wound.");
        }

        [Test]
        public async Task NoBane_SavedSixesStand()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1f).Within(0.0001f),
                "without Bane the saved 6s stand; only the original failed save lands.");
        }

        private async Task RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);

            // One failed save (1 wound) + two saved natural 6s (Bane's reroll targets) at save-needed 4.
            var failed = new List<FailedSaveInfo>
            {
                new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)),
            };
            var successful = new List<SuccessfulSaveInfo>
            {
                new SuccessfulSaveInfo(TestDice.Faces(6, 6), new PendingSaveRolls(TestDice.Faces(6, 6), 4)),
            };
            metadata.AddResult(new RollToSaveResults(successful, failed));

            await stage.Enter(metadata);
        }

        private static void AttachBane(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Bane", CoreRuleCatalog.Bane));

        private DataBinding<UnitData> MakeUnit(int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    specialRules: new List<SpecialRule>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
