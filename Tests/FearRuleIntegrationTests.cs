using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #021 Fear(X) — vertical slice through the REAL DetermineMeleeWinnerStage. Fear makes a unit count
    // as dealing +X extra wounds for the who-won-melee check only (no real wounds), so it can pull a loss
    // back to a tie or push a tie into a win — which flips who must then test morale. The stage fires
    // Melee_OnMeleeResolution per side; this attaches the catalog Fear rule and drives the stage directly,
    // mirroring DetermineMeleeWinnerStageTests (build context at full health, then deal wounds).
    [TestFixture]
    public class FearRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public async Task FearOnAttacker_TurnsTieIntoWin()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(2); // 2 vs 2 — a tie without Fear.
            AttachFear(attacker, 1);

            var (needsRoll, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon),
                "attacker's +1 (Fear) makes 3 vs 2 — it wins.");
            Assert.That(needsRoll, Is.True);
            Assert.That(doesntNeedRoll, Is.False);
        }

        [Test]
        public async Task FearOnDefender_PullsLossBackToTie()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2); // attacker dealt 2
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1); // defender dealt 1 — attacker wins without Fear
            AttachFear(defender, 1);

            var (needsRoll, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie),
                "defender's +1 (Fear) makes 2 vs 2 — a tie, so no loser tests morale.");
            Assert.That(doesntNeedRoll, Is.True);
            Assert.That(needsRoll, Is.False);
        }

        [Test]
        public async Task FearAmountComesFromArgument()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(3); // attacker dealt 3
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1); // defender dealt 1
            AttachFear(defender, 2); // +2 → 3 vs 3, a tie. Proves X is read from the rule's argument.

            var (_, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
            Assert.That(doesntNeedRoll, Is.True);
        }

        [Test]
        public async Task Fear_DealsNoRealWounds()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            AttachFear(attacker, 3);

            float defenderWoundsBefore = defender.GetValue().ModelBindings[0].GetValue().WoundsDealt;
            await RunDetermineWinner(combat);

            Assert.That(defender.GetValue().ModelBindings[0].GetValue().WoundsDealt, Is.EqualTo(defenderWoundsBefore),
                "Fear adjusts only the who-won comparison — it must not deal real wounds.");
        }

        // Helpers

        private static void AttachFear(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(
                new ResolvedRule("Fear", CoreRuleCatalog.Fear, new RuleArgument[] { new RuleArgument.Int(x) }));

        private async Task<(bool NeedsRoll, bool DoesntNeedRoll, DetermineMeleeWinnerResults Result)>
            RunDetermineWinner(CombatActionContext combat)
        {
            var stage = new DetermineMeleeWinnerStage(_ctx, new NoOpLayer<ICombatActionContext>());
            bool needsRoll = false, doesntNeedRoll = false;
            stage.OnNeedsRollToDecide.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION);
            stage.OnDoesntNeedRollToDecide.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION);
            stage.OnNeedsRollToDecide.OnWillActivate += _ => needsRoll = true;
            stage.OnDoesntNeedRollToDecide.OnWillActivate += _ => doesntNeedRoll = true;

            await stage.Enter(combat);

            combat.QueryForResult(out DetermineMeleeWinnerResults result);
            return (needsRoll, doesntNeedRoll, result);
        }

        // Two single-model units, 5 wounds each. Context built at full health so the start-of-melee
        // snapshots are full; tests then deal wounds to simulate the exchange.
        private (CombatActionContext Combat, DataBinding<UnitData> Attacker, DataBinding<UnitData> Defender)
            BuildMelee()
        {
            var attacker = MakeUnit("Attacker", new Position(0, 0));
            var defender = MakeUnit("Defender", new Position(2, 0));

            var combat = new CombatActionContext(_ctx, attacker, isMelee: true);
            combat.SetDefender(defender);
            return (combat, attacker, defender);
        }

        private DataBinding<UnitData> MakeUnit(string name, Position position)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: position, gameDataStore: _store);
            model.SetMaxWounds(5);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
