using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Blast(X) flows through the REAL RollToHitStage.
    // After the hit rolls land (and any injection rules fire), the stage folds the HitMultiplierSink and
    // appends synthetic hits to reach hits × X, with X capped PER HIT at the target unit's living model
    // count — "after other rules". FixedDiceRoller(6) makes every attack a natural 6, so an A_n weapon
    // lands n base hits and Blast(X) → n × min(X, models) total. The stage interprets no operation.
    [TestFixture]
    public class BlastRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position TargetPos   = new Position(10, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            // FixedFaceDiceRoller, not FixedDiceRoller: every die shows 6 AND the roll count is honoured,
            // so an A3 volley really reports 3 hits. FixedDiceRoller collapses any roll to TotalRolls == 1,
            // which would hide the per-hit stacking these tests exist to pin.
            _ctx = new TestGameContext(_store, new FixedFaceDiceRoller(6));
        }

        [Test]
        public async Task NoRules_OneBaseHit()
        {
            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), MakeUnit(TargetPos, modelCount: 5));
            Assert.That(TotalHits(result), Is.EqualTo(1f), "one natural 6 hits; no rule multiplies it.");
        }

        [Test]
        public async Task Blast3_TriplesHits_WithinModelCount()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 3);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos, modelCount: 5));

            Assert.That(TotalHits(result), Is.EqualTo(3f),
                "Blast(3) multiplies the 1 base hit to 3 (under the 5-model cap).");
        }

        [Test]
        public async Task Blast10_CapsAtTargetModelCount()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 10);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos, modelCount: 2));

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "Blast(10) would give 10 hits but is capped at the 2-model target unit.");
        }

        // #204: end-to-end, the Blast overflow group must carry a BlastMultiplier source (rule name +
        // multiplier) so the save-roll presentation can render "x3 (Blast)". Guards the RollToHitStage ->
        // save wiring the synthetic SaveBeatGroupingTests can't reach.
        [Test]
        public async Task Blast3_TagsOverflowGroupWithBlastSource()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 3);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos, modelCount: 5));

            SuccessfulHitInfo blast = result.SuccessfulHitList
                .First(h => h.Source.Kind == EHitSourceKind.BlastMultiplier);
            Assert.That(blast.Source.RuleName, Is.EqualTo("Blast"));
            Assert.That(blast.Source.Amount, Is.EqualTo(3f), "the multiplier is carried for the 'x3' text.");
        }

        // ── The per-hit cap stacks across hits (owner-ruled 2026-07-31) ───────────────────────────────
        //
        // Blast's model-count cap bounds what ONE hit fans out to; it is not a ceiling on the volley.
        // The engine used to cap the TOTAL, which turned an A3 Blast(3) into 3 hits no matter how many
        // landed — silently deleting save dice the defender owed. These three pin the arithmetic the
        // owner specified.

        [Test]
        public async Task A3Blast3_ThreeModelTarget_NineHits()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 3);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos, modelCount: 3), attacks: 3);

            Assert.That(TotalHits(result), Is.EqualTo(9f),
                "3 hits, each multiplied x3 (the cap is 3 models, so nothing is trimmed) = 9 save dice.");
        }

        [Test]
        public async Task A3Blast3_TwoModelTarget_SixHits()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 3);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos, modelCount: 2), attacks: 3);

            Assert.That(TotalHits(result), Is.EqualTo(6f),
                "each of the 3 hits is capped at the 2 living models, but they still stack: 3 x 2 = 6.");
        }

        [Test]
        public async Task A3Blast3_TwoModelTarget_TagsGroupWithTheEffectiveMultiplier()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 3);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos, modelCount: 2), attacks: 3);

            SuccessfulHitInfo blast = result.SuccessfulHitList
                .First(h => h.Source.Kind == EHitSourceKind.BlastMultiplier);
            Assert.That(blast.Source.Amount, Is.EqualTo(2f),
                "the save beat renders '3 hits x2 (Blast) = 6', so the group carries the multiplier that " +
                "actually applied, not the authored 3.");
        }

        // A dead model must not widen the cap: only LIVING models bound a hit's fan-out.
        [Test]
        public async Task A3Blast3_CapCountsOnlyLivingModels()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 3);

            DataBinding<UnitData> defender = MakeUnit(TargetPos, modelCount: 3);
            Kill(defender, index: 2);

            RollToHitResults result = await RunStage(attacker, defender, attacks: 3);

            Assert.That(TotalHits(result), Is.EqualTo(6f),
                "one of the 3 models is dead, so each hit caps at 2: 3 x 2 = 6, not 9.");
        }

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            int attacks = 1)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: attacks, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: attacks)); // a 6 clears a 4+ threshold

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList)
            {
                total += hit.HitCount;
            }
            return total;
        }

        // Deals lethal wounds to one model so the unit's LIVING count drops (the same primitive
        // MoraleUtilities.Rout uses); there is no direct "remove model" on a unit.
        private static void Kill(DataBinding<UnitData> unit, int index)
        {
            ModelData model = unit.GetValue().ModelBindings[index].GetValue();
            model.DealWounds(model.TotalWounds - model.WoundsDealt);
            Assert.That(model.GetIsAlive(), Is.False, "test setup: the model must actually be dead.");
        }

        private static void AttachBlast(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Blast", CoreRuleCatalog.Blast,
                new RuleArgument[] { new RuleArgument.Int(x) }));

        private DataBinding<UnitData> MakeUnit(Position position, int modelCount = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: position,
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
