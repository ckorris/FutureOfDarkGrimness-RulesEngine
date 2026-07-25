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

        // ── #157: a Takedown volley fires as single shots, each with its own pick ────────────────────────

        [Test]
        public async Task TakedownVolley_SplitsIntoSingleShots_EachPicksItsOwnModel()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 3, weaponName: "Sniper Rifle");
            AttachTakedown(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);

            var requester = new SequencedModelSelectionRequester(
                defender.ModelBindings()[0], defender.ModelBindings()[1], defender.ModelBindings()[2]);
            var ctx = new WoundTestContext(_store, requester);

            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            Weapon rifle = combat.AvailableWeapons.Keys.Single();
            combat.SetAttackWeapon(rifle, out int weaponCount);
            combat.SetDefender(defender);
            Assert.That(weaponCount, Is.EqualTo(3), "3 models carry the rifle, batched by name");

            // The split decision ChooseRangedAttackStage makes (non-consuming query), then the split itself.
            Assert.That(Rules.Dispatch.SightRuleQueries.TargetsIndividualModels(
                attacker.GetValue(), rifle, defender.GetValue(), ctx.RuleEvaluator), Is.True);
            combat.SplitPendingAttackIntoSingleShots();

            // Drive the real per-shot loop: each FireStage entry consumes one queued shot and runs its own
            // BuildTargetListStage, which asks for that shot's individual target.
            var picks = new List<DataBinding<ModelData>>();
            int shots = 0;
            while (combat.HasPendingAttack)
            {
                ICombatMetadata metadata = combat.ConsumeAttackIntoContext(ctx);
                Assert.That(metadata.WeaponCount, Is.EqualTo(1), "a split shot fires a single copy");

                var layer = new NoOpLayer<ICombatMetadata>();
                var stage = new BuildTargetListStage<ICombatMetadata>(ctx, layer);
                stage.NextStage.Bind("done");
                await stage.Enter(metadata);

                Assert.That(metadata.QueryForResult(out IndividualTargetResult result), Is.True,
                    "every shot gets its own Takedown pick");
                picks.Add(result.Model);
                shots++;
            }

            Assert.That(shots, Is.EqualTo(3), "the volley fired one attack per copy");
            Assert.That(picks, Is.EquivalentTo(defender.ModelBindings()),
                "the shots spread across three different chosen models");
        }

        [Test]
        public void NoTakedown_VolleyStaysBatched_OneAttackWithFullCount()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 3, weaponName: "Rifle");
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);
            var ctx = new WoundTestContext(_store, new CannedModelSelectionRequester(defender.ModelBindings()[0]));

            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            Weapon rifle = combat.AvailableWeapons.Keys.Single();
            combat.SetAttackWeapon(rifle, out int weaponCount);
            combat.SetDefender(defender);

            Assert.That(Rules.Dispatch.SightRuleQueries.TargetsIndividualModels(
                attacker.GetValue(), rifle, defender.GetValue(), ctx.RuleEvaluator), Is.False);

            ICombatMetadata metadata = combat.ConsumeAttackIntoContext(ctx);
            Assert.That(metadata.WeaponCount, Is.EqualTo(weaponCount), "unsplit volley fires as one batch");
            Assert.That(combat.HasPendingAttack, Is.False, "nothing left queued after the single consume");
        }

        // ── #276: trimmed volleys and burst indices ──────────────────────────────────────────────────

        [Test]
        public void TrimmedTakedownVolley_SplitsOnlyEligibleShots_WithBurstIndices()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 3, weaponName: "Sniper Rifle");
            AttachTakedown(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);
            var ctx = new WoundTestContext(_store, new CannedModelSelectionRequester(defender.ModelBindings()[0]));

            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            combat.SetAttackWeapon(combat.AvailableWeapons.Keys.Single(), out int weaponCount);
            combat.SetDefender(defender);
            Assert.That(weaponCount, Is.EqualTo(3));

            // The stage trims to the eligible-shooter count (one sniper is occluded, say), THEN splits.
            combat.TrimPendingAttack(2);
            combat.SplitPendingAttackIntoSingleShots();

            var burstIndices = new List<int>();
            while (combat.HasPendingAttack)
            {
                ICombatMetadata metadata = combat.ConsumeAttackIntoContext(ctx);
                Assert.That(metadata.WeaponCount, Is.EqualTo(1), "each split shot fires a single copy");
                burstIndices.Add(metadata.BurstShotIndex);
            }

            Assert.That(burstIndices, Is.EqualTo(new[] { 0, 1 }),
                "the trimmed volley fires only the eligible shots, each tagged with its burst position");
        }

        [Test]
        public async Task DeadDefenderMidVolley_RemainingShotsFizzle()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 3, weaponName: "Sniper Rifle");
            AttachTakedown(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 1);
            var ctx = new WoundTestContext(_store, new CannedModelSelectionRequester(defender.ModelBindings()[0]));

            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            combat.SetAttackWeapon(combat.AvailableWeapons.Keys.Single(), out _);
            combat.SetDefender(defender);
            combat.SplitPendingAttackIntoSingleShots();
            combat.ConsumeAttackIntoContext(ctx); // shot 1 fired...

            var model = defender.ModelBindings()[0].GetValue();
            model.DealWounds(model.TotalWounds - model.WoundsDealt); // ...and it killed the last model

            var stage = new DetermineMorePendingShotsStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.FireNextShot.Bind("fire");
            stage.OnVolleyComplete.Bind("volleyComplete");
            await stage.Enter(combat);

            Assert.That(combat.HasPendingAttack, Is.False,
                "the dead target's remaining queued shots are discarded, not fired into a corpse");
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

        private DataBinding<UnitData> MakeUnit(int modelCount, string? weaponName = null)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var weapons = new List<Weapon>();
                if (weaponName != null)
                {
                    // One instance per model — CombatActionContext batches identical weapons by comparer.
                    weapons.Add(new Weapon(weaponName, rangeInches: 24f, attacks: 1, armorPenetration: 0));
                }
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: weapons,
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

    // #157: resolves each successive SelectionRequest<ModelData> to the next pre-set model — one distinct
    // pick per shot of a split Takedown volley.
    internal sealed class SequencedModelSelectionRequester : IPlayerRequestByID
    {
        private readonly Queue<DataBinding<ModelData>> _picks;

        public SequencedModelSelectionRequester(params DataBinding<ModelData>[] picks) =>
            _picks = new Queue<DataBinding<ModelData>>(picks);

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is SelectionRequest<ModelData>)
            {
                return Task.FromResult((TReply)(object)_picks.Dequeue());
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
