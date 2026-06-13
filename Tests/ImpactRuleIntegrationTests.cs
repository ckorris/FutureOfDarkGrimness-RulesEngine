using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Impact(X) flows through the REAL melee charge-
    // contact stage. ResolveImpactHitsStage fires the OnChargeContact "when", folds the ImpactSink,
    // rolls X dice (each 2+ a hit), and runs those hits through the real save→wound sub-pipeline against
    // the defender — BEFORE any normal swings. FixedDiceRoller(2) makes every die a 2: each impact die
    // is a hit (2+), and each save (needed 4+ vs defense 4, AP 0) fails — so Impact(3) = 3 wounds.
    [TestFixture]
    public class ImpactRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public async Task ImpactCharger_DealsAutoHits_KillsLethalTarget()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2); // 2 wounds total; 3 impact wounds is lethal

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(0f),
                "Impact(3) deals 3 auto-hits → 3 wounds, wiping out the 2-wound defender before any swing.");
        }

        [Test]
        public async Task NoImpact_DefenderUnharmed()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1); // no Impact rule
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f),
                "with no Impact rule the stage skips — the defender takes no charge-contact hits.");
        }

        [Test]
        public async Task Impact_SubLethal_AssignsExactWoundCount()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5); // 5 wounds → 3 impact wounds is sub-lethal

            await RunStage(ctx, attacker, defender);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f),
                "Impact(3) produces 3 failed saves → 3 wounds to assign across the surviving unit.");
        }

        // Counter's companion facet: a charged Counter unit reduces the charger's Impact dice by 1 per
        // living Counter model. Impact(3) vs a 2-model Counter defender → 3 − 2 = 1 net die → 1 hit → 1
        // failed save → 1 wound (sub-lethal on a 2-wound unit, so it routes to the wound requester).
        [Test]
        public async Task CounterDefender_ReducesImpactDice_ByLivingModelCount()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);
            AttachCounter(defender);

            await RunStage(ctx, attacker, defender);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1f),
                "Impact(3) minus 2 Counter models = 1 net impact die → 1 hit → 1 wound to assign.");
        }

        // The reduction can cancel Impact entirely: Impact(3) vs a 3-model Counter defender → 3 − 3 = 0
        // net dice → the stage skips, exactly as if the charger had no Impact at all.
        [Test]
        public async Task CounterDefender_CanReduceImpactToZero()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);
            AttachCounter(defender);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(3f),
                "3 Counter models cancel Impact(3) → no charge-contact hits, defender untouched.");
        }

        // A Counter defender against a charger with NO Impact emits only the negative reduction; the net
        // dice go below zero, so the stage skips cleanly (the reduction never produces a phantom roll).
        [Test]
        public async Task CounterDefender_NoImpactCharger_NoEffect()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1); // no Impact rule
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);
            AttachCounter(defender);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f),
                "no Impact to reduce → the lone negative reduction skips the stage, defender untouched.");
        }

        private static void AttachCounter(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));

        private async Task RunStage(WoundTestContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new ResolveImpactHitsStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnImpactResolved.Bind("done");

            var context = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            await stage.Enter(context);
        }

        private static void AttachImpact(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(x) }));

        // Unlike FixedDiceRoller (which models a single die: TotalRolls is always 1), this returns
        // EVERY rolled die on a fixed face — so Roll(n) yields n dice there. Needed for Impact's
        // multi-die roll and the subsequent multi-save roll to scale (and stay integer/deterministic).
        private sealed class AllOnFaceDiceRoller : IDiceRoller
        {
            private readonly int _face;
            public AllOnFaceDiceRoller(int face) => _face = face;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[_face - 1] = rollCount;
                return new DiceResults(perSide);
            }
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
