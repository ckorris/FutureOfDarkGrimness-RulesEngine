using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P7 No Retreat - "When a unit where most models have this rule fails a morale test that causes
    // it to be Shaken or Routed, the test counts as passed instead. Then, roll as many dice as the number
    // of wounds it would take to fully destroy it, and for each result of 1-3 the unit takes one wound,
    // which can't be ignored."
    //
    // Three things here are easy to get subtly wrong and invisible to --validate-rules or RuleFireLint:
    //  * WHICH tests convert. TakeMoraleTest has four callers and only two end in Shaken/Rout; converting
    //    the others would cancel Mind Control's forced move and Fatigue Debuff's fatigue for free.
    //  * The already-Shaken automatic failure, which is the one that ROUTS. It returns before the hook
    //    fires, so without deliberate handling the rule could never do the thing its own text names.
    //  * The order against Fearless. A re-roll is free; converting first would charge a wound pool the
    //    unit never owed.
    [TestFixture]
    public class NoRetreatRuleIntegrationTests
    {
        private const string RuleName = "No Retreat";
        private const int Quality = 4;

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;

        // The shipped shape, hand-built because the engine suite cannot read the app's rule supplement.
        // NoRetreatShippedDataTests asserts the authored definition matches this.
        private static readonly SpecialRuleDefinition NoRetreat = new(RuleName,
            new[]
            {
                new HookEntry(EHookID.Morale_OnMoraleTestComplete,
                    new Condition.MostModelsHaveThisRule(),
                    new Effect.PassFailedMoraleTest(SelfWoundOnRollAtMost: 3),
                    ELifetime.UntilEndOfGame),
            },
            Array.Empty<ActivatedAbility>());

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(NoRetreat);
        }

        // ---- The conversion --------------------------------------------------------------------------

        [Test]
        public async Task AFailedTest_CountsAsPassed()
        {
            // Face 3 fails a Quality 4+ test, and is also inside the 1-3 self-wound band.
            var ctx = Context(face: 3);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5, withRule: 5);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                ctx, unit.GetValue(), Quality, failureCausesShakenOrRout: true);

            Assert.That(outcome.Passed, Is.True, "'the test counts as passed instead'");
            Assert.That(outcome.PassedViaConversion, Is.True, "...and the caller can tell it was bought");
        }

        [Test]
        public async Task WithoutTheRule_AFailedTestStaysFailed()
        {
            var ctx = Context(face: 3);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5, withRule: 0);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                ctx, unit.GetValue(), Quality, failureCausesShakenOrRout: true);

            Assert.That(outcome.Passed, Is.False);
            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(5f), "and nothing was self-inflicted");
        }

        [Test]
        public async Task TheConversion_CostsOneDiePerRemainingWound()
        {
            // "as many dice as the number of wounds it would take to fully destroy it" - 5 models at one
            // wound each is a five-die pool, and every die shows a wounding face here.
            var ctx = Context(face: 3);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5, withRule: 5);

            await MoraleUtilities.TakeMoraleTest(ctx, unit.GetValue(), Quality,
                failureCausesShakenOrRout: true);

            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(0f),
                "five dice, all in the 1-3 band, so five wounds - the whole unit");
        }

        [TestCase(3, 3f, TestName = "SelfWoundPool_AThreeWounds")]
        [TestCase(4, 0f, TestName = "SelfWoundPool_AFourDoesNot")]
        public async Task TheSelfWoundBand_IsOneToThree(int face, float expectedWounds)
        {
            // Driven through the already-Shaken path so no morale die is rolled and the face means exactly
            // one thing: what the self-wound pool shows.
            var ctx = Context(face);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, withRule: 3);
            Shake(unit);

            await MoraleUtilities.TakeMoraleTest(ctx, unit.GetValue(), Quality,
                failureCausesShakenOrRout: true);

            Assert.That(3f - unit.GetValue().RemainingWounds, Is.EqualTo(expectedWounds));
        }

        [Test]
        public async Task TheWoundsCannotBeIgnored()
        {
            // "which can't be ignored" - Regeneration ignores wounds on a 5+, and must not touch these.
            // They are applied straight through the casualty seam rather than the save/ignore pipeline.
            var ctx = Context(face: 3);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, withRule: 3);
            unit.GetValue().AttachRuleDefinition(
                new ResolvedRule("Regeneration", CoreRuleCatalog.Regeneration));
            Shake(unit);

            await MoraleUtilities.TakeMoraleTest(ctx, unit.GetValue(), Quality,
                failureCausesShakenOrRout: true);

            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(0f),
                "Regeneration does not apply - all three wounds land");
        }

        // ---- The already-Shaken automatic failure (owner-ruled 2026-07-29) ---------------------------

        [Test]
        public async Task AnAlreadyShakenUnit_ConvertsItsAutomaticFailure()
        {
            // The case that Routs, and the reason the rule exists. No die is rolled for the test itself.
            var ctx = Context(face: 4);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, withRule: 3);
            Shake(unit);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                ctx, unit.GetValue(), Quality, failureCausesShakenOrRout: true);

            Assert.That(outcome.Passed, Is.True,
                "an automatic failure is still 'a morale test that causes it to be Routed'");
            Assert.That(outcome.PassedViaConversion, Is.True);
        }

        [Test]
        public async Task AnAlreadyShakenUnitWithoutTheRule_StillAutomaticallyFails()
        {
            // The guard on the short-circuit this slice reached into: GF v3.5.1's "a Shaken unit always
            // fails its morale tests" must be untouched for everyone else.
            var ctx = Context(face: 6);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, withRule: 0);
            Shake(unit);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                ctx, unit.GetValue(), Quality, failureCausesShakenOrRout: true);

            Assert.That(outcome.Passed, Is.False,
                "a 6 cannot rescue a Shaken unit - no die is rolled at all");
        }

        // ---- Which tests are eligible ------------------------------------------------------------------

        [Test]
        public async Task AMoraleTestWithOtherConsequences_IsNotConverted()
        {
            // Mind Control / Fatigue Debuff and a spell's own morale test are not Shaken-or-Rout tests, and
            // the rule's wording does not cover them.
            var ctx = Context(face: 3);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5, withRule: 5);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                ctx, unit.GetValue(), Quality, failureCausesShakenOrRout: false);

            Assert.That(outcome.Passed, Is.False, "the failure stands, so the consequence lands");
            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(5f),
                "and no price is paid for a rescue that never happened");
        }

        // ---- Most, not all ------------------------------------------------------------------------------

        [TestCase(3, true, TestName = "Majority_ThreeOfFiveConverts")]
        [TestCase(2, false, TestName = "Majority_TwoOfFiveDoesNot")]
        public async Task ItTakesMOSTModels_NotAll_AndNotJustSome(int carriers, bool converts)
        {
            var ctx = Context(face: 3);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5, withRule: carriers);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                ctx, unit.GetValue(), Quality, failureCausesShakenOrRout: true);

            Assert.That(outcome.Passed, Is.EqualTo(converts),
                "'a unit where MOST models have this rule' - a strict majority of the living");
        }

        [Test]
        public async Task DeadModelsDoNotCountTowardTheMajority()
        {
            // Two carriers of five models is not a majority - until three non-carriers die.
            var ctx = Context(face: 3);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5, withRule: 2);
            foreach (ModelData corpse in unit.GetValue().Models.OfType<ModelData>().Skip(2))
            {
                corpse.DealWounds(corpse.TotalWounds);
            }

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                ctx, unit.GetValue(), Quality, failureCausesShakenOrRout: true);

            Assert.That(outcome.Passed, Is.True,
                "the two survivors are all of the living models, so 'most' is satisfied");
        }

        // ---- Order against Fearless ---------------------------------------------------------------------

        [Test]
        public async Task AFearlessRerollGoesFirst_SoNoWoundsArePaid()
        {
            // Fearless re-rolls a failed test and passes on a 4+. A face of 4 fails the Quality 4+... no -
            // 4 PASSES quality 4+. Use a Quality of 5 so the initial die fails and the fixed re-roll (4+)
            // still passes: the free rescue must win, leaving the wound pool unrolled.
            var ctx = Context(face: 4);
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5, withRule: 5);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Fearless", CoreRuleCatalog.Fearless));

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                ctx, unit.GetValue(), baseRollNeeded: 5, failureCausesShakenOrRout: true);

            Assert.That(outcome.Passed, Is.True);
            Assert.That(outcome.PassedViaReroll, Is.True, "Fearless rescued it for free");
            Assert.That(outcome.PassedViaConversion, Is.False);
            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(5f),
                "no conversion happened, so no self-wounds were owed");
        }

        // ---- Helpers -------------------------------------------------------------------------------------

        // FixedFaceDiceRoller, not FixedDiceRoller: the latter reports a single die however many were
        // rolled, which would hide the pool size entirely.
        private WoundTestContext Context(int face) =>
            new WoundTestContext(_store, new CapturingWoundRequester(), new FixedFaceDiceRoller(face),
                ruleResolver: _resolver);

        private static void Shake(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(new Token(TokenType.Shaken, 1,
                new TokenClearTrigger.ManualOnly()));

        // A unit of one-wound models, the first `withRule` of which carry No Retreat on the MODEL. Per-model
        // attachment is what lets these tests state a majority at all - a unit-level rule would always be
        // carried by every model.
        private DataBinding<UnitData> MakeUnit(int modelCount, int withRule)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(i, 0), _store);
                if (i < withRule) model.AttachRuleDefinition(new ResolvedRule(RuleName, NoRetreat));
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var player = new PlayerID(Guid.NewGuid());
            var unit = new UnitData(player, "Diehards", quality: Quality, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
