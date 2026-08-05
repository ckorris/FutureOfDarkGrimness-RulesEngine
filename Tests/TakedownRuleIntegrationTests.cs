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

        // ── #157/#340: a Takedown weapon fires ONE copy per weapon choice, each with its own pick ───────

        [Test]
        public async Task TakedownWeapon_FiresOneCopyPerChoice_EachPicksItsOwnModel()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 3, weaponName: "Sniper Rifle");
            AttachTakedown(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);

            var requester = new SequencedModelSelectionRequester(
                defender.ModelBindings()[0], defender.ModelBindings()[1], defender.ModelBindings()[2]);
            var ctx = new WoundTestContext(_store, requester);

            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            Weapon rifle = combat.AvailableWeapons.Keys.Single();

            // Three passes through the weapon picker, exactly as ChooseRangedAttackStage /
            // DetermineCanKeepShootingStage drive it: each commits one copy, fires it through the REAL
            // BuildTargetListStage (its own Takedown pick), and hands the rest back for the next pass.
            var picks = new List<DataBinding<ModelData>>();
            var burstIndices = new List<int>();
            var copiesLeftAfterEachPass = new List<int>();
            int shots = 0;
            while (combat.AvailableWeapons.ContainsKey(rifle))
            {
                combat.SetAttackWeapon(rifle, out _);
                combat.SetDefender(defender);

                // The decision ChooseRangedAttackStage makes (non-consuming query), then the commit itself.
                Assert.That(Rules.Dispatch.SightRuleQueries.TargetsIndividualModels(
                    attacker.GetValue(), rifle, defender.GetValue(), ctx.RuleEvaluator), Is.True);
                combat.AimPendingAttackOneCopyAtATime(out int copiesHeldBack);
                copiesLeftAfterEachPass.Add(copiesHeldBack);

                ICombatMetadata metadata = combat.ConsumeAttackIntoContext(ctx);
                Assert.That(metadata.WeaponCount, Is.EqualTo(1), "one rifle fires per choice");
                burstIndices.Add(metadata.BurstShotIndex);

                var layer = new NoOpLayer<ICombatMetadata>();
                var stage = new BuildTargetListStage<ICombatMetadata>(ctx, layer);
                stage.NextStage.Bind("done");
                await stage.Enter(metadata);

                Assert.That(metadata.QueryForResult(out IndividualTargetResult result), Is.True,
                    "every shot gets its own Takedown pick");
                picks.Add(result.Model);
                Assert.That(combat.HasPendingAttack, Is.False, "the pass queued exactly one attack");
                shots++;
            }

            Assert.That(shots, Is.EqualTo(3), "one attack per copy, three passes");
            Assert.That(copiesLeftAfterEachPass, Is.EqualTo(new[] { 2, 1, 0 }),
                "each pass hands the unfired rifles back to the pool");
            Assert.That(burstIndices, Is.EqualTo(new[] { 0, 1, 2 }),
                "#276: the shots are tagged in firing order so the attack beat rotates carriers");
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

        // ── #340: the copies not fired go back into the action's pool ────────────────────────────────

        [Test]
        public void TakedownCopies_ReturnToTheAvailablePool_UntilTheLastOneFires()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 3, weaponName: "Sniper Rifle");
            AttachTakedown(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);
            var ctx = new WoundTestContext(_store, new CannedModelSelectionRequester(defender.ModelBindings()[0]));

            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            Weapon rifle = combat.AvailableWeapons.Keys.Single();

            combat.SetAttackWeapon(rifle, out int weaponCount);
            combat.SetDefender(defender);
            Assert.That(weaponCount, Is.EqualTo(3), "3 models carry the rifle, batched by profile");

            combat.AimPendingAttackOneCopyAtATime(out int heldBack);
            Assert.That(heldBack, Is.EqualTo(2));
            Assert.That(combat.AvailableWeapons.TryGetValue(rifle, out int leftInPool), Is.True,
                "the weapon is offered again while copies remain - that is what lets each rifle pick its own target");
            Assert.That(leftInPool, Is.EqualTo(2));
            Assert.That(combat.AlreadyUsedWeapons[rifle], Is.EqualTo(1),
                "only the copy that actually fired counts as used");

            combat.ConsumeAttackIntoContext(ctx);
            combat.SetAttackWeapon(rifle, out _);
            combat.AimPendingAttackOneCopyAtATime(out heldBack);
            Assert.That(heldBack, Is.EqualTo(1));
            combat.ConsumeAttackIntoContext(ctx);

            combat.SetAttackWeapon(rifle, out _);
            combat.AimPendingAttackOneCopyAtATime(out heldBack);
            Assert.That(heldBack, Is.EqualTo(0), "the last rifle keeps nothing back");
            Assert.That(combat.AvailableWeapons.ContainsKey(rifle), Is.False,
                "with every copy fired the weapon leaves the pool and stops being offered");
        }

        [Test]
        public void DeadDefenderMidShoot_RemainingCopiesStayAvailableForANewTarget()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 3, weaponName: "Sniper Rifle");
            AttachTakedown(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 1);
            var ctx = new WoundTestContext(_store, new CannedModelSelectionRequester(defender.ModelBindings()[0]));

            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            Weapon rifle = combat.AvailableWeapons.Keys.Single();
            combat.SetAttackWeapon(rifle, out _);
            combat.SetDefender(defender);
            combat.AimPendingAttackOneCopyAtATime(out _);
            combat.ConsumeAttackIntoContext(ctx); // rifle 1 fired...

            var model = defender.ModelBindings()[0].GetValue();
            model.DealWounds(model.TotalWounds - model.WoundsDealt); // ...and it killed the last model

            Assert.That(combat.HasPendingAttack, Is.False,
                "nothing is queued at the corpse - the burst that used to fizzle here no longer exists");
            Assert.That(combat.AvailableWeapons[rifle], Is.EqualTo(2),
                "#340: the two rifles that had not fired are still available, and the weapon picker will " +
                "offer them a live target instead of discarding their shots");
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
