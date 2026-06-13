using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #064 — DetermineMeleeWinnerStage compares wounds dealt by each side (captured at melee start
    // vs. current) and routes: a clear winner needs a follow-up roll (OnNeedsRollToDecide), a tie
    // does not (OnDoesntNeedRollToDecide). It also records a DetermineMeleeWinnerResults for later
    // stages. The wound snapshots are taken when the CombatActionContext is built / SetDefender is
    // called, so these tests set max wounds first, build the context, then deal wounds.
    [TestFixture]
    public class DetermineMeleeWinnerStageTests
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
        public async Task AttackerDealtMoreWounds_AttackerWins_NeedsRoll()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1);

            var (needsRoll, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(needsRoll, Is.True);
            Assert.That(doesntNeedRoll, Is.False);
            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));
        }

        [Test]
        public async Task DefenderDealtMoreWounds_DefenderWins_NeedsRoll()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(2);

            var (needsRoll, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(needsRoll, Is.True);
            Assert.That(doesntNeedRoll, Is.False);
            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon));
        }

        [Test]
        public async Task BothDealtEqualWounds_Tie_DoesNotNeedRoll()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(2);

            var (needsRoll, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(needsRoll, Is.False);
            Assert.That(doesntNeedRoll, Is.True);
            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
        }

        // Equal (zero) wounds on both sides is still a tie — guards the boundary where neither side
        // lands a hit.
        [Test]
        public async Task NeitherDealtWounds_Tie_DoesNotNeedRoll()
        {
            var (combat, _, _) = BuildMelee();

            var (needsRoll, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(needsRoll, Is.False);
            Assert.That(doesntNeedRoll, Is.True);
            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
        }

        // Helpers

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

        // Two single-model units with 5 wounds each. Build the combat context AFTER setting wounds,
        // so the start-of-melee snapshots are full; tests then deal wounds to simulate the exchange.
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
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                
                initialPosition: position,
                gameDataStore: _store);
            model.SetMaxWounds(5);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
