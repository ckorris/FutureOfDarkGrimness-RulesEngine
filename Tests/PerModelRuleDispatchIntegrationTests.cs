using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #093 (foundational per-model dispatch): proves per-model rules
    // are GENERAL, not hero-only, and compose under EModelRuleScope.AllOwners for a pooled weapon batch —
    // a rule fires once when EVERY living owner shares it, is suppressed when only some owners carry it (no
    // leak onto the batch's shared roll), and never stacks per owner (the rulebook's "instances don't
    // stack"). Runs the REAL RollToHitStage. FixedDiceRoller(6) makes every die a natural 6, so Relentless
    // ("beyond 9\", each unmodified 6 scores an extra hit") reads as "2 hits when it fires, 1 when it
    // doesn't." Mirrors HeroPerModelRuleIntegrationTests' harness but builds a plain (non-hero) unit whose
    // models carry the rule directly on IModel.RuleDefinitions.
    [TestFixture]
    public class PerModelRuleDispatchIntegrationTests
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
        public async Task SharedPerModelRule_AllOwnersHaveIt_FiresOnce()
        {
            // Two models share one weapon and BOTH carry Relentless as a per-model rule. Under the old
            // sole-owner gate (owners.Count == 1) the multi-owner batch got NO per-model rules, so this
            // fired 1. The AllOwners intersection now fires the shared rule — once (no double-count).
            Weapon eliteBlade = new Weapon("Elite Blade", 48f, 1, 0);
            DataBinding<UnitData> attacker = MakeUnit(eliteBlade, modelCount: 2, ruleCarrierCount: 2);

            RollToHitResults result = await RunStage(attacker, MakeTarget(FarPos), eliteBlade);

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "every batch owner carries Relentless, so the shared per-model rule fires once (not zero, " +
                "as the old sole-owner gate did; not twice, which would violate no-stack).");
        }

        [Test]
        public async Task PerModelRule_NotSharedByAllOwners_Suppressed()
        {
            // Only one of the two batch owners carries Relentless. A pooled batch can't attribute which
            // dice are the carrier's, so AllOwners suppresses it — no leak onto the other owner's shots.
            Weapon sharedBlade = new Weapon("Shared Blade", 48f, 1, 0);
            DataBinding<UnitData> attacker = MakeUnit(sharedBlade, modelCount: 2, ruleCarrierCount: 1);

            RollToHitResults result = await RunStage(attacker, MakeTarget(FarPos), sharedBlade);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "the rule isn't shared by every owner of the pooled batch, so it does not fire (no leak).");
        }

        [Test]
        public async Task SharedPerModelRule_ThreeOwners_StillFiresOnce()
        {
            // Three owners all carry Relentless. Firing once per owner (a naive ModelID-keyed dedup) would
            // triple-count the batch's 6s; the correct no-stack result is a single firing → still 2 hits.
            Weapon eliteBlade = new Weapon("Elite Blade", 48f, 1, 0);
            DataBinding<UnitData> attacker = MakeUnit(eliteBlade, modelCount: 3, ruleCarrierCount: 3);

            RollToHitResults result = await RunStage(attacker, MakeTarget(FarPos), eliteBlade);

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "a shared argless rule fires once for the batch regardless of how many owners carry it.");
        }

        // --- harness (mirrors HeroPerModelRuleIntegrationTests) ---

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, Weapon weaponType)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var metadata = new CombatMetadata(_ctx, attacker, defender, weaponType, weaponCount: 1,
                attackerMoved: false, isMelee: false, isCharging: false);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1)); // a 6 clears a 4+ threshold

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private static float TotalHits(RollToHitResults results) =>
            results.SuccessfulHitList.Sum(hit => hit.HitCount);

        // A plain unit of modelCount models all carrying the same weapon; the first ruleCarrierCount of them
        // additionally carry Relentless directly on IModel.RuleDefinitions (no hero merge involved).
        private DataBinding<UnitData> MakeUnit(Weapon weapon, int modelCount, int ruleCarrierCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon> { weapon }, AttackerPos, _store);
                if (i < ruleCarrierCount)
                {
                    model.AttachRuleDefinition(new ResolvedRule("Relentless", CoreRuleCatalog.Relentless));
                }
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "EliteSquad",
                quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
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
