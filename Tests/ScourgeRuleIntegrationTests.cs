using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #431 Scourge (OPR 3.6): "For each unmodified result of 1 to block hits, enemies count as having taken 2 extra
    // wounds when checking who won melee, or if shooting wounds would trigger Morale Tests." Pinned end to end:
    //  - the REAL AssignWoundsStage banks 2 counted wounds per natural 1 on the struck unit, and deals none;
    //  - the REAL DetermineMeleeWinnerStage adds them to the struck unit's own taken total (#419: "the number of
    //    wounds each unit took"; Fear's "enemies count as having taken" polarity), and uses them up;
    //  - the REAL ResolveRangedMoraleStage counts them as wounds taken for the half-strength morale trigger - they
    //    alone can trigger the test - and uses them up;
    //  - the end-of-activation sweep clears whatever a skipped check left behind;
    //  - the virtual allocation behind "counts as having taken" (wounded models first, a joined hero last).
    [TestFixture]
    public class ScourgeRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private CapturingWoundRequester _requester = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new CapturingWoundRequester();
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller());
        }

        // --- the save roll banks counted wounds -------------------------------------------------------------

        [Test]
        public async Task TwoOnesToBlock_CountAsFourWoundsTaken_AndDealNone()
        {
            DataBinding<UnitData> defender = MakeUnit("Target", models: 5);

            float dealt = await RunSaves(ScourgeWeapon(), defender, failedFaces: new[] { 1, 1, 3 });

            Assert.That(CountedWounds.Peek(defender.GetValue()), Is.EqualTo(4f).Within(0.0001f),
                "two natural 1s x 2 counted wounds each");
            Assert.That(dealt, Is.EqualTo(3f).Within(0.0001f), "only the three failed saves are real wounds");
        }

        [Test]
        public async Task NoOnesToBlock_CountNothing()
        {
            DataBinding<UnitData> defender = MakeUnit("Target", models: 5);

            await RunSaves(ScourgeWeapon(), defender, failedFaces: new[] { 2, 3 });

            Assert.That(CountedWounds.Peek(defender.GetValue()), Is.EqualTo(0f));
        }

        [Test]
        public async Task WithoutScourge_OnesCountNothing()
        {
            DataBinding<UnitData> defender = MakeUnit("Target", models: 5);

            await RunSaves(new Weapon("Plain", 24f, 1, 0), defender, failedFaces: new[] { 1, 1 });

            Assert.That(CountedWounds.Peek(defender.GetValue()), Is.EqualTo(0f), "the control");
        }

        [Test]
        public async Task HistogramMode_CountsFractionalOnes()
        {
            // #100: under the probabilistic roller a save histogram is fractional; one third of a 1 is two thirds
            // of a counted wound, never rounded.
            DataBinding<UnitData> defender = MakeUnit("Target", models: 5);
            var failed = new DiceResults(new float[] { 1f / 3f, 0f, 2f / 3f, 0f, 0f, 0f });

            await RunSaves(ScourgeWeapon(), defender, failedHistogram: failed);

            Assert.That(CountedWounds.Peek(defender.GetValue()), Is.EqualTo(2f / 3f).Within(0.0001f));
        }

        [Test]
        public async Task CountedWounds_AccumulateAcrossAttacks()
        {
            DataBinding<UnitData> defender = MakeUnit("Target", models: 5);

            await RunSaves(ScourgeWeapon(), defender, failedFaces: new[] { 1 });
            await RunSaves(ScourgeWeapon(), defender, failedFaces: new[] { 1 });

            Assert.That(CountedWounds.Peek(defender.GetValue()), Is.EqualTo(4f).Within(0.0001f));
        }

        // --- melee result -----------------------------------------------------------------------------------

        [Test]
        public async Task InMelee_TheStrikersSideWins_OnTheStruckUnitsCountedWounds()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(1); // attacker dealt 1
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(3); // defender dealt 3 - defender wins
            CountedWounds.Add(defender.GetValue(), 4f);                    // two 1s to block vs Scourge

            DetermineMeleeWinnerResults result = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon),
                "1 + 4 counted vs 3: the defender counts as having taken more and loses");
            Assert.That(CountedWounds.Peek(defender.GetValue()), Is.EqualTo(0f), "used up by the check");
        }

        [Test]
        public async Task InMelee_WithoutCountedWounds_TheRealWoundsDecide()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(3);

            DetermineMeleeWinnerResults result = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon));
        }

        [Test]
        public async Task InMelee_TheChargersOwnCountedWounds_HelpTheDefender()
        {
            // A Scourge strike-back: the charger rolled the 1s, so the charger counts as having taken them.
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(3); // attacker dealt 3 - attacker wins
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1); // defender dealt 1
            CountedWounds.Add(attacker.GetValue(), 2f);

            DetermineMeleeWinnerResults result = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie),
                "3 vs 1 + 2 counted: the charger's own 1s to block pull its win back to a tie");
        }

        // --- shooting morale trigger ------------------------------------------------------------------------

        [Test]
        public async Task Shooting_CountedWoundsAlone_TriggerTheMoraleTest()
        {
            // 4 models, no real wound: two counted wounds leave it at 2 of 4 - half strength - so it tests. A
            // fixed 1 fails the Quality 4 test, so the outcome is observable as Shaken.
            _ctx = new WoundTestContext(_store, _requester, new FixedDiceRoller(1));
            DataBinding<UnitData> shooter = MakeUnit("Shooter", models: 1);
            DataBinding<UnitData> target = MakeUnit("Target", models: 4);
            CombatActionContext action = ShotAt(shooter, target);
            CountedWounds.Add(target.GetValue(), 2f);

            await RunRangedMorale(action);

            Assert.That(target.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True,
                "counted as having taken 2 wounds, it is at half strength and tests (and fails on a 1)");
            Assert.That(target.GetValue().ModelBindings.All(m => m.GetValue().GetIsAlive()), Is.True,
                "no real wounds were dealt");
            Assert.That(CountedWounds.Peek(target.GetValue()), Is.EqualTo(0f), "used up by the check");
        }

        [Test]
        public async Task Shooting_TooFewCountedWounds_NoMoraleTest()
        {
            _ctx = new WoundTestContext(_store, _requester, new FixedDiceRoller(1));
            DataBinding<UnitData> shooter = MakeUnit("Shooter", models: 1);
            DataBinding<UnitData> target = MakeUnit("Target", models: 5);
            CombatActionContext action = ShotAt(shooter, target);
            CountedWounds.Add(target.GetValue(), 2f); // 3 of 5 left: above half

            await RunRangedMorale(action);

            Assert.That(target.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        [Test]
        public async Task Shooting_WithoutCountedWounds_AnUnwoundedUnitDoesNotTest()
        {
            _ctx = new WoundTestContext(_store, _requester, new FixedDiceRoller(1));
            DataBinding<UnitData> shooter = MakeUnit("Shooter", models: 1);
            DataBinding<UnitData> target = MakeUnit("Target", models: 4);
            CombatActionContext action = ShotAt(shooter, target);

            await RunRangedMorale(action);

            Assert.That(target.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False, "the control");
        }

        [Test]
        public async Task Shooting_RealAndCountedWounds_AddUp()
        {
            // 6 models: one shot dead, then two counted wounds - 3 of 6 left, half strength.
            _ctx = new WoundTestContext(_store, _requester, new FixedDiceRoller(1));
            DataBinding<UnitData> shooter = MakeUnit("Shooter", models: 1);
            DataBinding<UnitData> target = MakeUnit("Target", models: 6);
            CombatActionContext action = ShotAt(shooter, target);
            target.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            CountedWounds.Add(target.GetValue(), 2f);

            await RunRangedMorale(action);

            Assert.That(target.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        // --- the end-of-activation sweep -------------------------------------------------------------------

        [Test]
        public void TheSweep_ClearsEveryUnitsCountedWounds()
        {
            DataBinding<UnitData> a = MakeUnit("A", models: 2);
            DataBinding<UnitData> b = MakeUnit("B", models: 2);
            CountedWounds.Add(a.GetValue(), 2f);
            CountedWounds.Add(b.GetValue(), 4f);

            CountedWounds.SweepAll(_ctx.TableState.Units.Objects);

            Assert.That(CountedWounds.Peek(a.GetValue()), Is.EqualTo(0f));
            Assert.That(CountedWounds.Peek(b.GetValue()), Is.EqualTo(0f));
        }

        [Test]
        public async Task TheEndOfAnyActivation_ClearsCountedWoundsOnEveryUnit()
        {
            // The counted wounds sit on the STRUCK unit, which is not the activated one - the ordinary
            // end-of-activation token sweep never visits it.
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> activated = MakeUnit("Charger", models: 2);
            DataBinding<UnitData> struck = MakeUnit("Victim", models: 2);
            CountedWounds.Add(struck.GetValue(), 4f);

            var turn = new SingleTurnContext(ctx, activated.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { activated });
            turn.ChooseUnitToActivate(activated);
            var stage = new ReconcileEndOfActivationStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(turn);

            Assert.That(CountedWounds.Peek(struck.GetValue()), Is.EqualTo(0f),
                "a melee that ended in a wipe-out never reached its result step - nothing may leak into the next activation");
        }

        // --- the virtual allocation ---------------------------------------------------------------------------

        [Test]
        public void VirtualAllocation_FinishesAWoundedModelFirst()
        {
            // 4 Tough(2) models, one with a wound taken: 3 counted wounds finish it (1) and one more (2) - 2 of 4
            // left, half. Spread from the top instead, 3 wounds would remove only one model.
            DataBinding<UnitData> unit = MakeUnit("Brutes", models: 4, tough: 2);
            unit.GetValue().ModelBindings[3].GetValue().DealWounds(1);

            Assert.That(unit.GetValue().GetIsAtHalfStrength(3f), Is.True);
            Assert.That(unit.GetValue().GetIsAtHalfStrength(2f), Is.False, "2 counted: the wounded model + half a fresh one");
            Assert.That(unit.GetValue().GetIsAtHalfStrength(0f), Is.EqualTo(unit.GetValue().GetIsAtHalfStrength()));
        }

        [Test]
        public void VirtualAllocation_ASingleModelReadsItsTough()
        {
            DataBinding<UnitData> monster = MakeUnit("Monster", models: 1, tough: 6);

            Assert.That(monster.GetValue().GetIsAtHalfStrength(2f), Is.False, "4 of 6 left");
            Assert.That(monster.GetValue().GetIsAtHalfStrength(3f), Is.True, "3 of 6 is half");
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private static Weapon ScourgeWeapon()
        {
            var weapon = new Weapon("Chain-Cannon", rangeInches: 30f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Scourge", WeaponRules36Definitions.Scourge));
            return weapon;
        }

        // Returns the real wounds the attack posts (failed saves).
        private async Task<float> RunSaves(Weapon weapon, DataBinding<UnitData> defender, int[]? failedFaces = null,
            DiceResults? failedHistogram = null)
        {
            DataBinding<UnitData> attacker = MakeUnit("Shooter", models: 1);
            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);

            DiceResults failed = failedHistogram ?? TestDice.Faces(failedFaces!);
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(),
                new List<FailedSaveInfo> { new FailedSaveInfo(failed, new PendingSaveRolls(failed, 4)) }));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            return result.TotalWoundsToAssign;
        }

        private async Task<DetermineMeleeWinnerResults> RunDetermineWinner(CombatActionContext combat)
        {
            var stage = new DetermineMeleeWinnerStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnNeedsRollToDecide.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION);
            stage.OnDoesntNeedRollToDecide.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION);
            await stage.Enter(combat);

            combat.QueryForResult(out DetermineMeleeWinnerResults result);
            return result;
        }

        // Two single-model units of 5 wounds each, the context built at full health (the FearRuleIntegrationTests
        // shape); tests then deal wounds to simulate the exchange.
        private (CombatActionContext Combat, DataBinding<UnitData> Attacker, DataBinding<UnitData> Defender) BuildMelee()
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", models: 1, tough: 5);
            DataBinding<UnitData> defender = MakeUnit("Defender", models: 1, tough: 5, x: 2f);
            var combat = new CombatActionContext(_ctx, attacker, isMelee: true);
            combat.SetDefender(defender);
            return (combat, attacker, defender);
        }

        private CombatActionContext ShotAt(DataBinding<UnitData> shooter, DataBinding<UnitData> target)
        {
            var action = new CombatActionContext(_ctx, shooter, isMelee: false);
            action.RegisterAttackedDefender(target);
            return action;
        }

        private async Task RunRangedMorale(CombatActionContext action)
        {
            var stage = new ResolveRangedMoraleStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.ToFinished.Bind(ResolveRangedMoraleStage.RESOLVE_RANGED_MORALE_FINISHED_TRANSITION);
            await stage.Enter(action);
        }

        private DataBinding<UnitData> MakeUnit(string name, int models, int tough = 1, float x = 0f)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(x + i * 1.2f, 0f), _store);
                if (tough > 1) model.SetMaxWounds(tough);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
