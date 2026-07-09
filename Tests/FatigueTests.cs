using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #020 — Fatigue. A unit that charges or strikes back in melee becomes Fatigued for the rest of the
    // round; while Fatigued (or Shaken, #089) it hits only on unmodified 6s in melee. Two halves:
    //   - the EFFECT: DetermineHitRollStage forces the melee threshold to 6 for a fatigued attacker;
    //   - the APPLICATION: ApplyFatigueStage tokens the units that actually fought, after the swings.
    [TestFixture]
    public class FatigueTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        // --- Utility ---

        [Test]
        public void ApplyFatigued_IsIdempotent_AndShakenCountsAsFatigued()
        {
            var unit = MakeUnit("U", modelCount: 1);

            FatigueUtilities.ApplyFatigued(unit.GetValue());
            FatigueUtilities.ApplyFatigued(unit.GetValue());

            Assert.That(unit.GetValue().Tokens.GetTokenCount(TokenType.Fatigued), Is.EqualTo(1),
                "applying twice must not stack the Fatigued token.");
            Assert.That(FatigueUtilities.CountsAsFatiguedInMelee(unit.GetValue()), Is.True);

            var shakenOnly = MakeUnit("S", modelCount: 1);
            MoraleUtilities.ApplyShaken(shakenOnly);
            Assert.That(shakenOnly.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False);
            Assert.That(FatigueUtilities.CountsAsFatiguedInMelee(shakenOnly.GetValue()), Is.True,
                "a Shaken unit always counts as fatigued in melee even without a Fatigued token.");
        }

        // --- Effect: hit threshold forced to 6 ---

        [Test]
        public async Task FatiguedMeleeAttacker_HitsOnlyOnSixes()
        {
            var attacker = MakeUnit("A", modelCount: 1);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            var result = await RunHitStage(attacker, MakeUnit("D", 1), isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(6),
                "base quality 4 is overridden to 6 because the attacker is fatigued in melee.");
        }

        [Test]
        public async Task ShakenMeleeAttacker_HitsOnlyOnSixes()
        {
            var attacker = MakeUnit("A", modelCount: 1);
            MoraleUtilities.ApplyShaken(attacker);

            var result = await RunHitStage(attacker, MakeUnit("D", 1), isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(6),
                "Shaken counts as fatigued in melee, so the threshold is forced to 6.");
        }

        [Test]
        public async Task UnfatiguedMeleeAttacker_UsesBaseQuality()
        {
            var attacker = MakeUnit("A", modelCount: 1);

            var result = await RunHitStage(attacker, MakeUnit("D", 1), isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4), "no fatigue → base quality 4.");
        }

        [Test]
        public async Task FatiguedShootingAttacker_Unaffected()
        {
            var attacker = MakeUnit("A", modelCount: 1);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            var result = await RunHitStage(attacker, MakeUnit("D", 1), isMelee: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "fatigue is melee-only — a shooting attack keeps base quality 4.");
        }

        // --- Application: ApplyFatigueStage ---

        [Test]
        public async Task ChargerIsFatigued_DefenderIsNotWithoutStrikeBack()
        {
            var charger = MakeUnit("Charger", 1);
            var defender = MakeUnit("Defender", 1);
            var combat = new CombatActionContext(_ctx, charger, isMelee: true, isCharging: true);
            combat.SetDefender(defender);

            await RunFatigue(combat);

            Assert.That(charger.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.True);
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False,
                "a defender that did not strike back is not fatigued.");
        }

        [Test]
        public async Task DefenderThatStruckBack_IsFatigued()
        {
            var charger = MakeUnit("Charger", 1);
            var defender = MakeUnit("Defender", 1);
            var combat = new CombatActionContext(_ctx, charger, isMelee: true, isCharging: true);
            combat.SetDefender(defender);
            combat.RegisterDefenderStruckBack();

            await RunFatigue(combat);

            Assert.That(charger.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.True);
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.True,
                "a defender that struck back becomes fatigued.");
        }

        [Test]
        public async Task CounterSwap_StillFatiguesTheOriginalCharger()
        {
            // Counter (DetermineStrikeOrderStage) calls SwapCombatRoles and never swaps back, so by the
            // time ApplyFatigueStage runs the AttackingUnit is the defender. The immutable ChargingUnit
            // capture must still fatigue the unit that actually charged.
            var charger = MakeUnit("Charger", 1);
            var defender = MakeUnit("Defender", 1);
            var combat = new CombatActionContext(_ctx, charger, isMelee: true, isCharging: true);
            combat.SetDefender(defender);
            combat.SwapCombatRoles();

            Assert.That(combat.AttackingUnit.Reference, Is.EqualTo(defender.Reference),
                "precondition: the swap made the defender the attacker.");

            await RunFatigue(combat);

            Assert.That(charger.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.True,
                "the original charger is fatigued for charging despite the role swap.");
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.True,
                "the swapped-in unit swung, so it is fatigued too.");
        }

        // --- Lifecycle: the token clears at end of round through the REAL sweep ---

        // Repro for the reported "fatigue never goes away on a unit that is charged every round":
        // strike-back fatigue must clear at the end-of-round sweep (ReconcileObjectivesStage walking
        // the real table state), and a fresh strike-back the following round re-applies exactly one
        // token — no stacking, no leftover from the prior round.
        [Test]
        public async Task StrikeBackFatigue_ClearsAtRoundEnd_AndReappliesSinglyNextRound()
        {
            var charger = MakeUnit("Charger", 1);
            var defender = MakeUnit("Defender", 1);

            // Round 1: charged, struck back — both fatigued (one token each).
            await RunChargeMelee(charger, defender);
            Assert.That(defender.GetValue().Tokens.GetTokenCount(TokenType.Fatigued), Is.EqualTo(1));
            Assert.That(charger.GetValue().Tokens.GetTokenCount(TokenType.Fatigued), Is.EqualTo(1));

            await RunRoundEndSweep();
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False,
                "Fatigued must clear at the end of the round.");
            Assert.That(charger.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False);

            // Round 2: charged again, strikes back again — fatigued again, still exactly one token.
            await RunChargeMelee(charger, defender);
            Assert.That(defender.GetValue().Tokens.GetTokenCount(TokenType.Fatigued), Is.EqualTo(1),
                "re-applying the following round must yield a single token, not a stack.");

            await RunRoundEndSweep();
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False,
                "the second round's token clears at that round's end too.");
        }

        // --- Helpers ---

        private async Task RunChargeMelee(DataBinding<UnitData> charger, DataBinding<UnitData> defender)
        {
            var combat = new CombatActionContext(_ctx, charger, isMelee: true, isCharging: true);
            combat.SetDefender(defender);
            combat.RegisterDefenderStruckBack();
            await RunFatigue(combat);
        }

        private Task RunRoundEndSweep()
        {
            var stage = new ReconcileObjectivesStage(_ctx, new NoOpLayer<IMainPhaseContext>());
            stage.ToReconcileEndOfTurn.Bind(
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN);
            stage.ToVictoryCalculation.Bind(
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION);
            return stage.Enter(new StubMainPhaseContext(_ctx));
        }

        private async Task RunFatigue(CombatActionContext combat)
        {
            var stage = new ApplyFatigueStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnFatigueApplied.Bind("done");
            await stage.Enter(combat);
        }

        private async Task<DetermineHitRollResults> RunHitStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, bool isMelee)
        {
            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: false);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True,
                "stage must store a DetermineHitRollResults in metadata.");
            return result;
        }

        private DataBinding<UnitData> MakeUnit(string name, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
