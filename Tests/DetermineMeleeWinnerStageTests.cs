using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #064 — DetermineMeleeWinnerStage compares the wounds each side TOOK (#419, Core Rules v3.6.0: "the number
    // of wounds each unit took during this activation, and the unit with the highest total loses") and routes:
    // a clear loser needs a follow-up roll (OnNeedsRollToDecide), a tie does not (OnDoesntNeedRollToDecide). It
    // also records a DetermineMeleeWinnerResults for later stages. A side's total is the wounds it had taken
    // earlier this activation (the wound ledger, snapshotted when the CombatActionContext is built /
    // SetDefender is called) plus the remaining-wounds drop since that snapshot, so these tests set max wounds
    // first, build the context, then deal wounds.
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
        public async Task DefenderTookMoreWounds_AttackerWins_NeedsRoll()
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
        public async Task AttackerTookMoreWounds_DefenderWins_NeedsRoll()
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
        public async Task BothTookEqualWounds_Tie_DoesNotNeedRoll()
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
        public async Task NeitherTookWounds_Tie_DoesNotNeedRoll()
        {
            var (combat, _, _) = BuildMelee();

            var (needsRoll, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(needsRoll, Is.False);
            Assert.That(doesntNeedRoll, Is.True);
            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
        }

        // ── #419: "during this activation" ─────────────────────────────────────────────────────────

        // A wound the attacker took BEFORE the melee began this activation (a Dangerous Terrain test on its
        // charge move) sits on the wound ledger when the context snapshots it, and joins its total.
        [Test]
        public async Task AWoundTakenEarlierThisActivation_Counts()
        {
            var attacker = MakeUnit("Attacker", new Position(0, 0));
            var defender = MakeUnit("Defender", new Position(2, 0));
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            WoundsTakenThisActivation.Add(attacker.GetValue(), 1f);   // the charge move's terrain test

            var combat = new CombatActionContext(_ctx, attacker, isMelee: true);
            combat.SetDefender(defender);
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(2); // 2 vs 2 in the melee itself

            var (needsRoll, _, result) = await RunDetermineWinner(combat);

            Assert.That(combat.AttackerWoundsTakenBeforeMelee, Is.EqualTo(1f));
            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon),
                "3 (1 earlier + 2) vs 2 - the attacker took more");
            Assert.That(needsRoll, Is.True);
        }

        // The defender's earlier wounds are snapshotted when it is set (before any Impact hit).
        [Test]
        public async Task AWoundTheDefenderTookEarlierThisActivation_Counts()
        {
            var attacker = MakeUnit("Attacker", new Position(0, 0));
            var defender = MakeUnit("Defender", new Position(2, 0));
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            WoundsTakenThisActivation.Add(defender.GetValue(), 1f);   // a Breath Attack before the charge

            var combat = new CombatActionContext(_ctx, attacker, isMelee: true);
            combat.SetDefender(defender);
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1);

            var (_, _, result) = await RunDetermineWinner(combat);

            Assert.That(combat.DefenderWoundsTakenBeforeMelee, Is.EqualTo(1f));
            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));
        }

        // The melee's own wounds land on the ledger too (ApplyWoundsStage feeds it), but the stage reads the
        // ledger only as it stood at the snapshot - each wound counts once. Here the defender takes 2 and the
        // attacker 1 in the melee, ledgered like the real seam does; the result line must say 1 and 2, not 2 and 4.
        [Test]
        public async Task WoundsLedgeredDuringTheMelee_AreCountedOnce()
        {
            var log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), textOutput: log);
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            WoundsTakenThisActivation.Add(defender.GetValue(), 2f);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            WoundsTakenThisActivation.Add(attacker.GetValue(), 1f);

            var (_, _, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));
            Assert.That(log.Entries.Select(entry => entry.Message), Has.Member(
                "Melee result: Attacker took 1 wound, Defender took 2 wounds - Defender took more and loses the melee."),
                "each wound read once");
        }

        // A mirror match puts two same-named units in one melee; the result line must still say which one lost.
        // With no player slots both owners read alike, so the seats tell them apart.
        [Test]
        public async Task SameNamedUnits_TheResultLineTellsThemApart()
        {
            var log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), textOutput: log);
            var attacker = MakeUnit("Warriors", new Position(0, 0));
            var defender = MakeUnit("Warriors", new Position(2, 0));
            var combat = new CombatActionContext(_ctx, attacker, isMelee: true);
            combat.SetDefender(defender);
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(1);

            await RunDetermineWinner(combat);

            Assert.That(log.Entries.Select(entry => entry.Message), Has.Member(
                "Melee result: Warriors (attacker) took 0 wounds, Warriors (defender) took 1 wound - " +
                "Warriors (defender) took more and loses the melee."));
        }

        // The ledger is a melee-result input only: a shooting context never reads it.
        [Test]
        public void ARangedContext_DoesNotSnapshotTheLedger()
        {
            var attacker = MakeUnit("Attacker", new Position(0, 0));
            var defender = MakeUnit("Defender", new Position(2, 0));
            WoundsTakenThisActivation.Add(attacker.GetValue(), 1f);
            WoundsTakenThisActivation.Add(defender.GetValue(), 1f);

            var shooting = new CombatActionContext(_ctx, attacker, isMelee: false);
            shooting.SetDefender(defender);

            Assert.That(shooting.AttackerWoundsTakenBeforeMelee, Is.EqualTo(0f));
            Assert.That(shooting.DefenderWoundsTakenBeforeMelee, Is.EqualTo(0f));
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
