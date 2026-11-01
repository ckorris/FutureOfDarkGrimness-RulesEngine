using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
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
    // #422 (Core Rules v3.6.0): the Fatigue gate, Counter's lost impact facet and its new home Brace(X) are
    // pinned here; Brace's own counting rules live in BraceRuleIntegrationTests.
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

        // #197 misc - Heavy Impact ("Impact(X) with hits that have AP(1)"). AllOnFaceDiceRoller(4): each
        // impact die is a 4 (a hit, 2+), each save is a 4. Against defense 4, an AP-0 save of 4 HOLDS
        // (4 >= 4) but an AP-1 save needs 5 and FAILS - so the only thing separating these two tests is the
        // AP the impact hits now carry.
        [Test]
        public async Task HeavyImpact_HitsCarryAP_TheSaveFailsWhereCoreImpactWouldHold()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(4));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachHeavyImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5); // 5 wounds, survives 3 wounds

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f),
                "Heavy Impact(3): 3 hits, each save (4) fails against AP(1) -> 3 wounds.");
        }

        [Test]
        public async Task CoreImpact_AtTheSameDice_TheSaveHolds()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(4));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3); // AP 0
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(5f),
                "core Impact(3): 3 hits, but each save of 4 holds at AP 0 -> no wounds. The contrast pins " +
                "that Heavy Impact's extra wounds come from the AP, not the dice.");
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

        // #422 - Core Rules v3.6.0 Impact: "..., roll X dice, unless this unit is Fatigued." A charger that
        // already fought this round rolls nothing: the stage skips straight on, no die is thrown, the defender
        // is untouched. (ImpactCharger_DealsAutoHits_KillsLethalTarget above is the fresh control.)
        [Test]
        public async Task FatiguedImpactCharger_RollsNoDice()
        {
            var roller = new RecordingDiceRoller(face: 2);
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), roller);
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);

            await RunStage(ctx, attacker, defender);

            Assert.That(roller.RollCounts, Is.Empty, "a Fatigued charger's Impact rolls no dice at all.");
            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f), "so the defender takes no impact hits.");
        }

        // Shaken always counts as Fatigued in melee (#089); the gate is the shared predicate.
        [Test]
        public async Task ShakenImpactCharger_RollsNoDice()
        {
            var roller = new RecordingDiceRoller(face: 2);
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), roller);
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            attacker.GetValue().Tokens.AddToken(
                new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);

            await RunStage(ctx, attacker, defender);

            Assert.That(roller.RollCounts, Is.Empty, "Shaken counts as Fatigued in melee - no impact dice.");
            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f));
        }

        // Heavy Impact "counts as having Impact(X)", so it inherits the clause; the shipped definition carries
        // the gate (pinned app-side in HeavyImpactShippedDataTests). Here the engine shape with the gate.
        [Test]
        public async Task FatiguedHeavyImpactCharger_RollsNoDice()
        {
            var roller = new RecordingDiceRoller(face: 4);
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), roller);
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Heavy Impact", GatedHeavyImpactDefinition,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            FatigueUtilities.ApplyFatigued(attacker.GetValue());
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(ctx, attacker, defender);

            Assert.That(roller.RollCounts, Is.Empty);
            Assert.That(defender.RemainingWounds(), Is.EqualTo(5f));
        }

        // #422 - 3.5 Counter also cost the charger one Impact die per Counter model; 3.6 moved that to
        // Brace(X), so a Counter defender no longer touches the dice. Impact(3) vs a 2-model Counter unit is
        // all 3 dice -> 3 hits -> 3 wounds to assign (it was 1 under 3.5).
        [Test]
        public async Task CounterDefender_NoLongerReducesImpactDice()
        {
            var requester = new CapturingWoundRequester();
            var roller = new RecordingDiceRoller(face: 2);
            var ctx = new WoundTestContext(_store, requester, roller);
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachCounter(defender);

            await RunStage(ctx, attacker, defender);

            Assert.That(roller.RollCounts.First(), Is.EqualTo(3f), "Counter cancels no impact dice in 3.6.");
            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f));
        }

        // The impact reduction now belongs to Brace(X): "Enemies charging this unit get -X total Impact
        // rolls". Brace(1) attached to a 2-model unit -> Impact(3) - 2 = 1 net die -> 1 hit -> 1 wound.
        [Test]
        public async Task BraceDefender_ReducesImpactDice_ByLivingModelCount()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);
            AttachBrace(defender, x: 1);

            await RunStage(ctx, attacker, defender);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1f),
                "Impact(3) minus Brace(1) on 2 models = 1 net impact die -> 1 hit -> 1 wound to assign.");
        }

        // The reduction can cancel Impact entirely: Impact(3) vs Brace(1) on 3 models -> 0 net dice -> the
        // stage skips, exactly as if the charger had no Impact at all.
        [Test]
        public async Task BraceDefender_CanReduceImpactToZero()
        {
            var roller = new RecordingDiceRoller(face: 2);
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), roller);
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);
            AttachBrace(defender, x: 1);

            await RunStage(ctx, attacker, defender);

            Assert.That(roller.RollCounts, Is.Empty, "3 Brace models cancel Impact(3) - nothing to roll.");
            Assert.That(defender.RemainingWounds(), Is.EqualTo(3f));
        }

        // A Brace defender against a charger with NO Impact emits only the negative reduction; the net dice go
        // below zero, so the stage skips cleanly (the reduction never produces a phantom roll).
        [Test]
        public async Task BraceDefender_NoImpactCharger_NoEffect()
        {
            var roller = new RecordingDiceRoller(face: 2);
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), roller);
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1); // no Impact rule
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);
            AttachBrace(defender, x: 1);

            await RunStage(ctx, attacker, defender);

            Assert.That(roller.RollCounts, Is.Empty);
            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f),
                "no Impact to reduce -> the lone negative reduction skips the stage, defender untouched.");
        }

        private static void AttachCounter(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));

        private static void AttachBrace(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Brace", CoreRuleCatalog.Brace,
                new RuleArgument[] { new RuleArgument.Int(x) }));

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

        // Heavy Impact ships as supplement data; here it's built inline as its engine shape - core Impact's
        // charge-contact entry with AP(1) on the impact hits.
        private static readonly SpecialRuleDefinition HeavyImpactDefinition = new SpecialRuleDefinition(
            "Heavy Impact",
            new[]
            {
                new HookEntry(EHookID.Melee_OnChargeContact, new Condition.Always(),
                    new Effect.ChargeImpactHits(new ValueSource.Arg(0), ArmorPenetration: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        // The shipped Heavy Impact since #422: the same entry gated like core Impact ("counts as having Impact(X)").
        private static readonly SpecialRuleDefinition GatedHeavyImpactDefinition = new SpecialRuleDefinition(
            "Heavy Impact",
            new[]
            {
                new HookEntry(EHookID.Melee_OnChargeContact,
                    new Condition.Not(new Condition.CountsAsFatiguedInMelee()),
                    new Effect.ChargeImpactHits(new ValueSource.Arg(0), ArmorPenetration: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        private static void AttachHeavyImpact(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Heavy Impact", HeavyImpactDefinition,
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

        // AllOnFaceDiceRoller that also records every roll's size, in order: the first entry is the impact
        // dice the stage threw, and an empty list proves it threw none.
        private sealed class RecordingDiceRoller : IDiceRoller
        {
            private readonly int _face;
            public List<float> RollCounts { get; } = new();
            public RecordingDiceRoller(int face) => _face = face;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                RollCounts.Add(rollCount);
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
