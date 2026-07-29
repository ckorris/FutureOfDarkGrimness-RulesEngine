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
    // #197 Inquisitorial Agent - "Once per game, if all models in this unit have this rule, it may be
    // activated even if it had already activated this round (stops being fatigued when activated for the
    // second time). Only up to one third of the units in the army with this rule at the beginning of the
    // game (rounding up) may use it in a single round."
    //
    // The second activation itself is Martial Prowess's, already proven by ReactivateRuleIntegrationTests.
    // What is new, and what these pin, is the ARMY-WIDE per-round cap - the first rule in the corpus whose
    // availability depends on what its SIBLINGS did - plus the fatigue rider.
    //
    // The roster is counted straight off ArmyData.UnitBindings, which is append-only: a destroyed unit
    // stays in the list, so the count is the game-start roster without any snapshot. Two tests below pin
    // that (casualties do not shrink the quota) because it is the whole reason no new state was needed.
    [TestFixture]
    public class InquisitorialAgentRuleIntegrationTests
    {
        private const string RuleName = "Inquisitorial Agent";
        private static readonly TokenType UsedMarker = new("AbilityUsed:" + RuleName);

        private GameDataStore _store = null!;
        private PlayerID _player;

        // The shipped shape, hand-built because the engine suite cannot read the app's rule supplement.
        // InquisitorialAgentShippedDataTests asserts the authored definition matches this.
        private static readonly SpecialRuleDefinition InquisitorialAgent = new(RuleName,
            Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnNextActivatorRequested, new Cost.OncePerGame(),
                    new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                    new Effect.Reactivate(ClearsFatigue: true, ArmyRoundQuotaDivisor: 3),
                    new Condition.AllModelsHaveThisRule()),
            });

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        // ---- The army-wide quota -----------------------------------------------------------------------

        [TestCase(3, 1, TestName = "Quota_ThreeAgentsAllowOne")]
        [TestCase(4, 2, TestName = "Quota_FourAgentsAllowTwo")]
        [TestCase(6, 2, TestName = "Quota_SixAgentsAllowTwo")]
        [TestCase(7, 3, TestName = "Quota_SevenAgentsAllowThree")]
        public async Task OnlyOneThirdOfTheAgents_RoundingUp_MayReactivateInARound(int agents, int expected)
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: true));
            List<DataBinding<UnitData>> units = MakeAgents(agents);
            SingleRoundContext round = MakeRound(ctx);
            foreach (DataBinding<UnitData> unit in units) round.MarkUnitAsActivated(unit);

            await RunStage(ctx, round);

            Assert.That(Reactivated(units), Is.EqualTo(expected),
                $"{agents} agents -> ceil({agents}/3) = {expected} second activations this round");
        }

        [Test]
        public async Task DeadAgentsStillCountTowardTheRoster()
        {
            // "the units in the army with this rule AT THE BEGINNING OF THE GAME". Four agents, two of them
            // wiped out, still buys two reactivations - the quota must not shrink as the army takes losses,
            // which is exactly when the rule matters.
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: true));
            List<DataBinding<UnitData>> units = MakeAgents(4);
            foreach (DataBinding<UnitData> dead in units.Take(2))
            {
                foreach (ModelData model in dead.GetValue().Models.OfType<ModelData>())
                {
                    model.DealWounds(model.TotalWounds);
                }
            }

            // Dead units are never in the unactivated pool, so only the survivors are marked.
            SingleRoundContext round = MakeRound(ctx);
            foreach (DataBinding<UnitData> unit in units.Where(u => u.GetValue().GetIsAlive()))
            {
                round.MarkUnitAsActivated(unit);
            }

            await RunStage(ctx, round);

            Assert.That(Reactivated(units), Is.EqualTo(2),
                "two survivors, but the roster is still the four the army started with - a live-only " +
                "count would have allowed only ceil(2/3) = 1");
        }

        [Test]
        public async Task TheCapIsPerRound_NotPerGame()
        {
            // The used-this-round stamp clears at round end, so a fresh round re-opens the quota for the
            // agents that have not yet spent their own once-per-game gate.
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: true));
            List<DataBinding<UnitData>> units = MakeAgents(4);
            SingleRoundContext round = MakeRound(ctx);
            foreach (DataBinding<UnitData> unit in units) round.MarkUnitAsActivated(unit);

            await RunStage(ctx, round);
            Assert.That(Reactivated(units), Is.EqualTo(2), "round one: two of four");

            // Round end: the per-round stamps clear, the once-per-game markers do not.
            foreach (DataBinding<UnitData> unit in units)
            {
                unit.GetValue().Tokens.RemoveTokens(TokenType.ReactivatedThisRound);
            }
            SingleRoundContext next = MakeRound(ctx);
            foreach (DataBinding<UnitData> unit in units) next.MarkUnitAsActivated(unit);

            await RunStage(ctx, next);

            Assert.That(Reactivated(units), Is.EqualTo(2),
                "round two: the OTHER two agents, whose once-per-game gate is still open");
        }

        [Test]
        public async Task AnAgentThatAlreadySpentItsOncePerGame_IsNotOfferedAgain()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: true));
            List<DataBinding<UnitData>> units = MakeAgents(3);
            foreach (DataBinding<UnitData> spent in units)
            {
                spent.GetValue().Tokens.AddToken(new Token(UsedMarker, 1, new TokenClearTrigger.ManualOnly()));
            }

            SingleRoundContext round = MakeRound(ctx);
            foreach (DataBinding<UnitData> unit in units) round.MarkUnitAsActivated(unit);

            await RunStage(ctx, round);

            Assert.That(Reactivated(units), Is.Zero,
                "the army's quota is open, but every agent has already used its own once-per-game");
        }

        [Test]
        public async Task DecliningCostsTheArmyNothing()
        {
            // The stamp goes on at acceptance, not at the offer - a declined offer must not burn the
            // army's slot for the round.
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: false));
            List<DataBinding<UnitData>> units = MakeAgents(3);
            SingleRoundContext round = MakeRound(ctx);
            foreach (DataBinding<UnitData> unit in units) round.MarkUnitAsActivated(unit);

            await RunStage(ctx, round);

            Assert.That(units.Any(u => u.GetValue().Tokens.HasToken(TokenType.ReactivatedThisRound)), Is.False,
                "nothing was spent");
            Assert.That(units.Any(u => u.GetValue().Tokens.HasToken(UsedMarker)), Is.False);
        }

        // ---- The fatigue rider -------------------------------------------------------------------------

        [Test]
        public async Task TheSecondActivation_ClearsFatigue()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: true));
            List<DataBinding<UnitData>> units = MakeAgents(3);
            DataBinding<UnitData> agent = units[0];
            FatigueUtilities.ApplyFatigued(agent.GetValue());

            SingleRoundContext round = MakeRound(ctx);
            round.MarkUnitAsActivated(agent);

            await RunStage(ctx, round);

            Assert.That(agent.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False,
                "'stops being fatigued when activated for the second time'");
        }

        [Test]
        public async Task MartialProwess_KeepsItsFatigue_AndIgnoresTheQuota()
        {
            // The control for both riders. Martial Prowess is the same reactivation with neither clause, so
            // a shared implementation that always cleared fatigue - or always applied a quota - would show
            // up here rather than in the rule that asked for them.
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: true));
            List<DataBinding<UnitData>> veterans = new List<DataBinding<UnitData>>();
            for (int i = 0; i < 3; i++) veterans.Add(MakeUnit($"Veterans {i}", CoreRuleCatalog.MartialProwess));
            foreach (DataBinding<UnitData> v in veterans) FatigueUtilities.ApplyFatigued(v.GetValue());

            SingleRoundContext round = MakeRound(ctx);
            foreach (DataBinding<UnitData> v in veterans) round.MarkUnitAsActivated(v);

            await RunStage(ctx, round);

            Assert.That(Reactivated(veterans), Is.EqualTo(3),
                "no quota is declared, so all three reactivate");
            Assert.That(veterans.All(v => v.GetValue().Tokens.HasToken(TokenType.Fatigued)), Is.True,
                "Martial Prowess says nothing about fatigue, so the token stays");
        }

        // ---- Helpers -----------------------------------------------------------------------------------

        // What the quota actually governs: how many units took a second activation. Read off the stamp
        // rather than the unactivated pool, which also holds the ruleless filler each round needs.
        private static int Reactivated(IEnumerable<DataBinding<UnitData>> units) =>
            units.Count(u => u.GetValue().Tokens.HasToken(TokenType.ReactivatedThisRound));

        private static async Task RunStage(TriggeredMoveTestContext ctx, SingleRoundContext round)
        {
            var stage = new DeterminePlayerTurnStage(ctx, new NoOpLayer<ISingleRoundContext>());
            stage.OnDeterminedPlayerTurn.Bind("done");
            stage.OnNoPlayersLeft.Bind("done");
            await stage.Enter(round);
        }

        // Every test here marks all its agents activated, and DeterminePlayerTurnStage only reaches the
        // reactivation offer while the player still HAS a turn - so each round gets a ruleless filler that
        // stays in the pool. It carries no Inquisitorial Agent, so it does not count toward the roster.
        private SingleRoundContext MakeRound(TriggeredMoveTestContext ctx)
        {
            var team = new TeamData(1, new List<PlayerID> { _player });
            _store.Create(team);
            MakeUnit("Conscripts", CoreRuleCatalog.Fearless);
            return new SingleRoundContext(ctx, new List<ITeam> { team });
        }

        private List<DataBinding<UnitData>> MakeAgents(int count)
        {
            var units = new List<DataBinding<UnitData>>();
            for (int i = 0; i < count; i++) units.Add(MakeUnit($"Agents {i}", InquisitorialAgent));
            return units;
        }

        private DataBinding<UnitData> MakeUnit(string name, SpecialRuleDefinition definition)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(5f, 5f), _store);
            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                    { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(definition.Name, definition));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
