using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #021 Fearless + morale-roll modifiers — through the shared, rule-aware MoraleUtilities.TakeMoraleTest
    // (used by every morale path) and the RollForMoraleStage wiring. Fearless: a failed morale test gets a
    // fresh die that passes on 4+ regardless of Quality. FixedDiceRoller returns one value for every roll,
    // so Quality 5 + a fixed 4 fails the initial test (4 < 5) but clears the 4+ re-roll — the precise shape
    // that exercises the second chance. Modifiers: a +1 morale rule lowers the threshold instead.
    [TestFixture]
    public class FearlessRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // --- MoraleUtilities.TakeMoraleTest (the shared engine) ---

        [Test]
        public async Task Fearless_FailedTest_PassesOnRerollOfFourPlus()
        {
            var unit = MakeUnit(quality: 5);
            AttachFearless(unit);

            var outcome = await MoraleUtilities.TakeMoraleTest(Ctx(die: 4), unit.GetValue(), baseRollNeeded: 5);

            Assert.That(outcome.Passed, Is.True);
            Assert.That(outcome.PassedViaReroll, Is.True, "4 fails the Quality-5 test but clears the Fearless 4+ re-roll.");
        }

        [Test]
        public async Task Fearless_FailedTest_FailsWhenRerollBelowFour()
        {
            var unit = MakeUnit(quality: 5);
            AttachFearless(unit);

            var outcome = await MoraleUtilities.TakeMoraleTest(Ctx(die: 3), unit.GetValue(), baseRollNeeded: 5);

            Assert.That(outcome.Passed, Is.False, "3 fails both the Quality-5 test and the 4+ re-roll.");
        }

        [Test]
        public async Task Fearless_PassedInitialTest_DoesNotReroll()
        {
            var unit = MakeUnit(quality: 4);
            AttachFearless(unit);

            var outcome = await MoraleUtilities.TakeMoraleTest(Ctx(die: 4), unit.GetValue(), baseRollNeeded: 4);

            Assert.That(outcome.Passed, Is.True);
            Assert.That(outcome.PassedViaReroll, Is.False, "the initial test already passed — no second chance needed.");
        }

        [Test]
        public async Task WithoutFearless_FailedTest_StaysFailed()
        {
            var unit = MakeUnit(quality: 5);

            var outcome = await MoraleUtilities.TakeMoraleTest(Ctx(die: 4), unit.GetValue(), baseRollNeeded: 5);

            Assert.That(outcome.Passed, Is.False, "no morale rule → a failed test has no second chance.");
        }

        [Test]
        public async Task MoraleModifier_LowersTheThreshold()
        {
            // An inline +1 morale rule (the shape a future Courage would take) riding Morale_OnPreMoraleTest.
            var brave = new SpecialRuleDefinition("Brave",
                new[]
                {
                    new HookEntry(EHookID.Morale_OnPreMoraleTest,
                        new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                        ELifetime.ThisActivation),
                },
                System.Array.Empty<ActivatedAbility>());
            var unit = MakeUnit(quality: 5);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Brave", brave));

            var outcome = await MoraleUtilities.TakeMoraleTest(Ctx(die: 4), unit.GetValue(), baseRollNeeded: 5);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4), "+1 morale lowers the Quality-5 threshold to 4.");
            Assert.That(outcome.Passed, Is.True);
            Assert.That(outcome.PassedViaReroll, Is.False, "it passed the (eased) initial test, not a re-roll.");
        }

        // The catalog Courage rule is exactly that +1-morale shape — lock it in against the real definition.
        [Test]
        public async Task Courage_LowersTheMoraleThreshold()
        {
            var unit = MakeUnit(quality: 5);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Courage", CoreRuleCatalog.Courage));

            var outcome = await MoraleUtilities.TakeMoraleTest(Ctx(die: 4), unit.GetValue(), baseRollNeeded: 5);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4), "Courage's +1 lowers the Quality-5 threshold to 4.");
            Assert.That(outcome.Passed, Is.True, "the eased test passes on a 4.");
        }

        // --- Fearless reaches the wound-driven path too (shooting / dangerous terrain), not just melee ---

        [Test]
        public async Task Fearless_PreventsAWoundDrivenMoraleFailure()
        {
            var unit = MakeUnit(quality: 5, modelCount: 4);
            AttachFearless(unit);
            KillModels(unit, 2); // reduced to half strength (2 of 4 living)

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(Ctx(die: 4), unit, remainingWoundsBefore: 4f);

            Assert.That(result, Is.True, "Fearless re-roll (4+) passes, so the half-strength unit is unaffected.");
            Assert.That(unit.GetValue().GetIsAlive(), Is.True);
            Assert.That(unit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.False,
                "a passed test (via Fearless) leaves the unit un-Shaken.");
        }

        [Test]
        public async Task WithoutFearless_WoundDrivenTestFails_IsShaken()
        {
            var unit = MakeUnit(quality: 5, modelCount: 4);
            KillModels(unit, 2);

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(Ctx(die: 4), unit, remainingWoundsBefore: 4f);

            Assert.That(result, Is.False);
            Assert.That(unit.GetValue().GetIsAlive(), Is.True,
                "no Fearless → the failed half-strength test makes it Shaken, not Routed (shooting/terrain never Routs).");
            Assert.That(unit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.True);
        }

        // --- RollForMoraleStage wiring (the melee loss path) ---

        [Test]
        public async Task RollForMoraleStage_Fearless_RoutesToPassed()
        {
            var ctx = Ctx(die: 4);
            var loser = MakeUnit(quality: 5, modelCount: 5);
            AttachFearless(loser);
            var combat = new CombatActionContext(ctx, MakeUnit(quality: 4), isMelee: true);
            combat.SetDefender(loser);
            combat.AddResult(new DetermineMoraleSaveNeededResult(loser.GetValue().Quality, loser));

            var stage = new RollForMoraleStage(ctx, new NoOpLayer<ICombatActionContext>());
            bool passed = false, failed = false;
            stage.OnMoralePassed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_PASSED_TRANSITION);
            stage.OnMoraleFailed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_FAILED_TRANSITION);
            stage.OnMoralePassed.OnWillActivate += _ => passed = true;
            stage.OnMoraleFailed.OnWillActivate += _ => failed = true;

            await stage.Enter(combat);

            Assert.That(passed, Is.True, "Fearless turns the failed melee morale test into a pass.");
            Assert.That(failed, Is.False);
        }

        [Test]
        public async Task RollForMoraleStage_DestroyedLoser_SkipsTestAndPasses()
        {
            var ctx = Ctx(die: 1); // a die that WOULD fail any test, to prove none is rolled
            var loser = MakeUnit(quality: 4, modelCount: 3);
            KillModels(loser, 3); // wiped out in the melee
            var combat = new CombatActionContext(ctx, MakeUnit(quality: 4), isMelee: true);
            combat.SetDefender(loser);
            combat.AddResult(new DetermineMoraleSaveNeededResult(loser.GetValue().Quality, loser));

            var stage = new RollForMoraleStage(ctx, new NoOpLayer<ICombatActionContext>());
            bool passed = false, failed = false;
            stage.OnMoralePassed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_PASSED_TRANSITION);
            stage.OnMoraleFailed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_FAILED_TRANSITION);
            stage.OnMoralePassed.OnWillActivate += _ => passed = true;
            stage.OnMoraleFailed.OnWillActivate += _ => failed = true;

            await stage.Enter(combat);

            Assert.That(passed, Is.True, "a destroyed unit skips morale and continues with no penalty.");
            Assert.That(failed, Is.False, "even with a die that would fail, no test is rolled for a dead unit.");
        }

        // Helpers

        private TestGameContext Ctx(int die) => new TestGameContext(_store, new FixedDiceRoller(die));

        private static void AttachFearless(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Fearless", CoreRuleCatalog.Fearless));

        private static void KillModels(DataBinding<UnitData> unit, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var model = unit.GetValue().ModelBindings[i].GetValue();
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
            }
        }

        private DataBinding<UnitData> MakeUnit(int quality, int modelCount = 1)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0), gameDataStore: _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "U", quality: quality, defense: 4,
                modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
