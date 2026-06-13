using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Takedown flows through the REAL shooting stages.
    // BuildTargetListStage fires the OnShootTargetsSelected "when"; on a queued TargetIndividualModel it
    // asks the attacker to pick one model and stashes an IndividualTargetResult. AssignWoundsStage then
    // confines all wounds to that single model — capped at its wounds, no carry-over ("a unit of [1]").
    [TestFixture]
    public class TakedownRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public async Task BuildTargetList_WithTakedown_StashesChosenModel()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachTakedown(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);
            DataBinding<ModelData> wanted = defender.ModelBindings()[1];

            var ctx = new WoundTestContext(_store, new CannedModelSelectionRequester(wanted));
            var metadata = await RunBuildTargetList(ctx, attacker, defender);

            Assert.That(metadata.QueryForResult(out IndividualTargetResult result), Is.True,
                "Takedown must stash an IndividualTargetResult.");
            Assert.That(result.Model, Is.EqualTo(wanted), "the stashed model is the one the attacker picked.");
        }

        [Test]
        public async Task BuildTargetList_NoTakedown_NoIndividualTarget()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);

            var ctx = new WoundTestContext(_store, new CannedModelSelectionRequester(defender.ModelBindings()[0]));
            var metadata = await RunBuildTargetList(ctx, attacker, defender);

            Assert.That(metadata.QueryForResult(out IndividualTargetResult _), Is.False,
                "without Takedown, no model is singled out.");
        }

        [Test]
        public async Task AssignWounds_WithIndividualTarget_ConfinesToModelAndCaps()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5); // 1 wound each
            DataBinding<ModelData> target = defender.ModelBindings()[0];

            // NullPlayerRequester is never called — Takedown bypasses the assign-wounds request.
            var ctx = new WoundTestContext(_store, new CannedModelSelectionRequester(target));
            AssignWoundsResults result = await RunAssignWounds(ctx, attacker, defender, target, failedSaves: 2);

            Assert.That(result.PendingWounds.Count, Is.EqualTo(1), "only the single targeted model receives wounds.");
            Assert.That(result.PendingWounds[0].Model, Is.EqualTo(target));
            Assert.That(result.PendingWounds[0].Wounds, Is.EqualTo(1f),
                "2 failed saves, but a 1-wound model takes only 1 — no carry-over to the rest of the unit.");
        }

        private async Task<CombatMetadata> RunBuildTargetList(WoundTestContext ctx,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new BuildTargetListStage<ICombatMetadata>(ctx, layer);
            stage.NextStage.Bind("done");

            var metadata = MakeMetadata(ctx, attacker, defender);
            await stage.Enter(metadata);
            return metadata;
        }

        private async Task<AssignWoundsResults> RunAssignWounds(WoundTestContext ctx,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, DataBinding<ModelData> target, int failedSaves)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new AssignWoundsStage<ICombatMetadata>(ctx, layer);
            stage.NextStage.Bind("done");

            var metadata = MakeMetadata(ctx, attacker, defender);
            metadata.AddResult(new IndividualTargetResult(target));

            var failedList = new List<FailedSaveInfo>();
            for (int i = 0; i < failedSaves; i++)
            {
                failedList.Add(new FailedSaveInfo(RulesHarness.TestDice.Faces(1),
                    new PendingSaveRolls(RulesHarness.TestDice.Faces(1), 4)));
            }
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failedList));

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True,
                "Stage must store an AssignWoundsResults in metadata.");
            return result;
        }

        private CombatMetadata MakeMetadata(WoundTestContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            return new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1); // isMelee:false
        }

        private static void AttachTakedown(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Takedown", CoreRuleCatalog.Takedown));

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

    // Resolves a SelectionRequest<ModelData> to a pre-set model (the attacker's Takedown pick).
    internal sealed class CannedModelSelectionRequester : IPlayerRequestByID
    {
        private readonly DataBinding<ModelData> _pick;

        public CannedModelSelectionRequester(DataBinding<ModelData> pick) => _pick = pick;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is SelectionRequest<ModelData>)
            {
                return Task.FromResult((TReply)(object)_pick);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
