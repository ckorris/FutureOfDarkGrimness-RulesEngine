using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice tests for the Tough wound-ordering rule (GDF/OPR): wounds must be assigned to an
    // already-wounded (but still alive) model before any fresh model, and a wounded model must be
    // finished before moving on. The mandatory part is enforced in the AssignWoundsResults constructor
    // (pre-assignment), and AssignWoundsStage skips the player prompt when that leaves no real choice.
    //
    // Two layers: direct construction tests on AssignWoundsResults (the rule itself), and stage tests
    // that prove the prompt is suppressed / emitted correctly (reusing the WoundRuleIntegrationTests
    // harness — CapturingWoundRequester, WoundTestContext).
    [TestFixture]
    public class ToughWoundOrderingRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private CapturingWoundRequester _requester = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new CapturingWoundRequester();
            _ctx = new WoundTestContext(_store, _requester);
        }

        // --- Direct construction: the rule itself ---

        [Test]
        public void Construct_PreAssignsToAlreadyWoundedModelFirst()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, woundsPerModel: 3);
            PreWound(unit, modelIndex: 0, wounds: 1); // 2 remaining

            var results = new AssignWoundsResults(unit, totalWoundsToAssign: 2);

            Assert.That(results.PendingWounds[0].Wounds, Is.EqualTo(2f),
                "both wounds funnel into the already-wounded model, finishing it.");
            Assert.That(results.PendingWounds[1].Wounds, Is.EqualTo(0f), "fresh models are untouched...");
            Assert.That(results.PendingWounds[2].Wounds, Is.EqualTo(0f), "...until the wounded one is dealt with.");
            Assert.That(results.TotalAssignedWounds, Is.EqualTo(2f));
            Assert.That(results.IsFinishedAssigning, Is.True);
            Assert.That(results.HasRemainingChoice, Is.False, "pool spent on the wounded model — no decision left.");
        }

        [Test]
        public void Construct_PreAssignmentFillsWoundedModelThenLeavesChoiceForTheRest()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, woundsPerModel: 3);
            PreWound(unit, modelIndex: 0, wounds: 1); // 2 remaining

            var results = new AssignWoundsResults(unit, totalWoundsToAssign: 4);

            Assert.That(results.PendingWounds[0].Wounds, Is.EqualTo(2f),
                "the wounded model is filled to death first (its 2 remaining)...");
            Assert.That(results.TotalAssignedWounds, Is.EqualTo(2f), "...and only that much is pre-assigned.");
            Assert.That(results.IsFinishedAssigning, Is.False);
            Assert.That(results.RemainingAssignableModelCount, Is.EqualTo(2),
                "the two fresh models remain as choices for the leftover 2 wounds.");
            Assert.That(results.HasRemainingChoice, Is.True);
        }

        [Test]
        public void Construct_NoWoundedModels_NothingPreAssigned()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, woundsPerModel: 3);

            var results = new AssignWoundsResults(unit, totalWoundsToAssign: 2);

            Assert.That(results.TotalAssignedWounds, Is.EqualTo(0f), "no prior damage → no forced assignment.");
            Assert.That(results.PendingWounds.TrueForAll(p => p.Wounds == 0f), Is.True);
            Assert.That(results.HasRemainingChoice, Is.True);
        }

        // Documents the deferred sub-choice: when more than one model is already wounded and the pool
        // can't finish them all, they fill in unit-list order (no player choice of which absorbs the
        // shortfall).
        [Test]
        public void Construct_MultipleWoundedModels_FillInListOrder()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, woundsPerModel: 3);
            PreWound(unit, modelIndex: 0, wounds: 1); // 2 remaining
            PreWound(unit, modelIndex: 1, wounds: 2); // 1 remaining

            var results = new AssignWoundsResults(unit, totalWoundsToAssign: 2);

            Assert.That(results.PendingWounds[0].Wounds, Is.EqualTo(2f), "first wounded model fills first...");
            Assert.That(results.PendingWounds[1].Wounds, Is.EqualTo(0f),
                "...exhausting the pool before the second wounded model — list order, no choice offered.");
        }

        // --- Through the stage: prompt suppression / emission ---

        [Test]
        public async Task Stage_PreAssignmentConsumesPool_NoPlayerPrompt()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3, woundsPerModel: 3);
            PreWound(defender, modelIndex: 0, wounds: 1); // 2 remaining

            await RunStage(attacker, defender, failedSaves: 2); // 2 wounds → both forced onto the wounded model

            Assert.That(_requester.Captured, Is.Null,
                "the mandatory pre-assignment placed every wound, so the stage never asks the player.");
        }

        [Test]
        public async Task Stage_ChoiceRemainsAfterPreAssignment_PlayerPrompted()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3, woundsPerModel: 3);
            PreWound(defender, modelIndex: 0, wounds: 1); // 2 remaining

            await RunStage(attacker, defender, failedSaves: 4); // 2 finish the wounded model, 2 are a real choice

            Assert.That(_requester.Captured, Is.Not.Null, "leftover wounds across fresh models → a real decision.");
            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(4f),
                "the request still carries the full wound count; pre-assignment is reconstructed by the resolver.");
        }

        // --- Helpers ---

        private async Task RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender, int failedSaves)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);

            var failedList = new List<FailedSaveInfo>();
            for (int i = 0; i < failedSaves; i++)
            {
                failedList.Add(new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)));
            }
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failedList));

            await stage.Enter(metadata);
        }

        private static void PreWound(DataBinding<UnitData> unit, int modelIndex, int wounds) =>
            ((ModelData)unit.GetValue().Models[modelIndex]).DealWounds(wounds);

        private DataBinding<UnitData> MakeUnit(int modelCount, int woundsPerModel = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                if (woundsPerModel != 1) model.SetMaxWounds(woundsPerModel); // Tough(woundsPerModel)
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
