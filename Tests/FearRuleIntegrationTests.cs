using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #021 Fear(X) — vertical slice through the REAL DetermineMeleeWinnerStage. #419, Core Rules v3.6.0: "When
    // in melee with this model, enemies count as having taken +X wounds when checking who won." Fear adds X to
    // the ENEMY's taken total for the who-won-melee check only (no real wounds), so it can pull a loss back to a
    // tie or push a tie into a win — which flips who must then test morale. The stage fires
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
                "attacker's Fear(1): the defender counts as having taken 3 vs 2 — it loses.");
            Assert.That(needsRoll, Is.True);
            Assert.That(doesntNeedRoll, Is.False);
        }

        [Test]
        public async Task FearOnDefender_PullsLossBackToTie()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2); // defender took 2
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1); // attacker took 1 — attacker wins without Fear
            AttachFear(defender, 1);

            var (needsRoll, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie),
                "defender's Fear(1): the attacker counts as having taken 2 vs 2 — a tie, so no loser tests morale.");
            Assert.That(doesntNeedRoll, Is.True);
            Assert.That(needsRoll, Is.False);
        }

        [Test]
        public async Task FearAmountComesFromArgument()
        {
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(3); // defender took 3
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1); // attacker took 1
            AttachFear(defender, 2); // the attacker counts +2 → 3 vs 3, a tie. Proves X is read from the rule's argument.

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

        // ── #175: Fear(X) is per-MODEL ("When in melee with this model, enemies count as..."), and a joined
        // hero's rules ride the hero MODEL (#006 slice F). Before #175 this stage dispatched with no
        // models list, so a hero-only Fear contributed nothing at all. ──────────────────────────────

        [Test]
        public async Task FearOnJoinedHeroModel_CountsForTheWholeUnit()
        {
            var (combat, attacker, defender) = BuildMelee(attackerModelCount: 3);
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(2); // 2 vs 2 — a tie without Fear.
            AttachFearToModel(attacker, modelIndex: 2, x: 1); // the joined hero carries it, the host doesn't

            var (needsRoll, _, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon),
                "a joined hero's Fear(1) makes the unit 3 vs 2 - it wins.");
            Assert.That(needsRoll, Is.True);
        }

        // (X) rules are exempt from the rulebook's no-stack clause, so the host unit's rule and the
        // hero model's rule are two sources and sum. Pinned by arithmetic: only +3 lands on a tie.
        [Test]
        public async Task FearOnHostUnitAndOnJoinedHero_BothCount()
        {
            var (combat, attacker, defender) = BuildMelee(attackerModelCount: 3);
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2); // defender took 2
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(5); // attacker took 5
            AttachFear(attacker, 1);                          // host unit
            AttachFearToModel(attacker, modelIndex: 2, x: 2); // joined hero

            var (_, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie),
                "host's +1 and hero's +2 both count: 2+3 vs 5 is a tie. (+1 or +2 alone would lose.)");
            Assert.That(doesntNeedRoll, Is.True);
        }

        // The other half of "once per source": a UNIT-level Fear counts once for the unit, not once per
        // model carrying it. Nothing to rule on in the rulebook here - it's the engine's existing
        // treatment of unit-scoped rules, and passing the models list must not change it.
        [Test]
        public async Task UnitLevelFear_CountsOnce_NotPerModel()
        {
            var (combat, attacker, defender) = BuildMelee(attackerModelCount: 3);
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(2); // defender took 2
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(3); // attacker took 3
            AttachFear(attacker, 1);

            var (_, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie),
                "+1 once makes 3 vs 3; once per model (+3) would have made it a win.");
            Assert.That(doesntNeedRoll, Is.True);
        }

        // Living models only, matching every other model-aware dispatch site: a hero killed during this
        // melee is no longer there to frighten anyone.
        [Test]
        public async Task FearOnDeadHeroModel_ContributesNothing()
        {
            var (combat, attacker, defender) = BuildMelee(attackerModelCount: 3);
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(4); // defender took 4
            AttachFearToModel(attacker, modelIndex: 2, x: 2);
            attacker.GetValue().ModelBindings[2].GetValue().DealWounds(5); // the Fear carrier dies; attacker took 5

            var (needsRoll, _, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon),
                "the dead carrier's +2 must not apply: 4 vs 5 stands (with it, the attacker would win 6 vs 5).");
            Assert.That(needsRoll, Is.True);
        }

        // ── #419: the 3.6 wording reaches the player ─────────────────────────────────────────────────

        // The log names the ENEMY as the one that "counts as having taken" the Fear wounds, and the result line
        // states what each side took.
        [Test]
        public async Task FearLog_SaysTheEnemyCountsAsHavingTakenTheWounds()
        {
            var log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), textOutput: log);
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            AttachFear(attacker, 2);

            await RunDetermineWinner(combat);

            var lines = log.Entries.Select(entry => entry.Message).ToList();
            Assert.That(lines, Has.Member("Defender counts as having taken +2 wounds (Attacker's Fear)."));
            Assert.That(lines, Has.Member(
                "Melee result: Attacker took 1 wound, Defender took 3 wounds - Defender took more and loses the melee."));
            Assert.That(lines.All(line => line.All(c => c <= '\u00FF')), Is.True, "game text is ASCII/Latin-1 only");
        }

        // Fear on BOTH sides lands on both opponents: equal Fear on an even exchange stays a tie.
        [Test]
        public async Task EqualFearOnBothSides_EvenExchange_StaysATie()
        {
            var log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), textOutput: log);
            var (combat, attacker, defender) = BuildMelee();
            defender.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            attacker.GetValue().ModelBindings[0].GetValue().DealWounds(1);
            AttachFear(attacker, 1);
            AttachFear(defender, 1);

            var (_, doesntNeedRoll, result) = await RunDetermineWinner(combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
            Assert.That(doesntNeedRoll, Is.True);
            Assert.That(log.Entries.Select(entry => entry.Message), Has.Member(
                "Melee result: Attacker took 2 wounds, Defender took 2 wounds - a tie, nobody tests morale."));
        }

        // Helpers

        // Mirrors HeroJoinResolver (#006 slice F), which moves a joined hero's own rules onto the hero MODEL.
        private static void AttachFearToModel(DataBinding<UnitData> unit, int modelIndex, int x) =>
            unit.GetValue().ModelBindings[modelIndex].GetValue().AttachRuleDefinition(
                new ResolvedRule("Fear", CoreRuleCatalog.Fear, new RuleArgument[] { new RuleArgument.Int(x) }));

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

        // Two units, 5 wounds per model, single-model unless a test needs somewhere to put a joined hero
        // (#175). Context built at full health so the start-of-melee snapshots are full; tests then deal
        // wounds to simulate the exchange.
        private (CombatActionContext Combat, DataBinding<UnitData> Attacker, DataBinding<UnitData> Defender)
            BuildMelee(int attackerModelCount = 1)
        {
            var attacker = MakeUnit("Attacker", new Position(0, 0), attackerModelCount);
            var defender = MakeUnit("Defender", new Position(2, 0));

            var combat = new CombatActionContext(_ctx, attacker, isMelee: true);
            combat.SetDefender(defender);
            return (combat, attacker, defender);
        }

        private DataBinding<UnitData> MakeUnit(string name, Position position, int modelCount = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(position.x + i, position.z), gameDataStore: _store);
                model.SetMaxWounds(5);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
