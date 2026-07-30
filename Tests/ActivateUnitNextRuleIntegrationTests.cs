using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P19, the out-of-order activation primitive.
    //
    // The primitive is deliberately rule-agnostic - the engine never mentions Coordinate, the corpus rule
    // that needs it ("at the end of this unit's activation, another friendly unit within 12in that hasn't
    // activated yet may be activated immediately; may not be used if this unit was activated via
    // Coordinate"). What is pinned here is the mechanism, in three parts:
    //
    //  - PRODUCER: ReconcileEndOfActivationStage picks a target and flags it with TokenType.ActivatesNext.
    //  - CONSUMERS: DeterminePlayerTurnStage points the alternation cursor at the flagged unit's OWNER
    //    instead of advancing, and ChooseUnitToActivateStage activates it with no menu.
    //  - The POOL stays authoritative: a flag on a unit that is dead or has already activated is ignored,
    //    which is what makes a marker left behind by any path degrade to a no-op instead of a stuck round.
    //
    // The ally case is a first-class fixture, not an afterthought: the owner keeps control (owner ruling,
    // 2026-07-30), so the cursor has to cross to another player's seat, and that is the only reason
    // PointAt moves the team index as well as the player index.
    [TestFixture]
    public class ActivateUnitNextRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;
        private PlayerID _ally;
        private PlayerID _enemy;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
            _ally = new PlayerID(System.Guid.NewGuid());
            _enemy = new PlayerID(System.Guid.NewGuid());
        }

        // ── Dispatch: the ability and its anti-chain condition ───────────────────────────────────────────

        [Test]
        public void Dispatch_TheAbilityEmitsTheGrantForItsTarget()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> bearer = MakeUnit(_player, "General", new Position(5f, 5f), ActivateNextRule());
            DataBinding<UnitData> friend = MakeUnit(_player, "Infantry", new Position(9f, 5f));

            AbilityOffer offer = ctx.RuleEvaluator
                .GatherOffers(new ActivationEndContext(bearer.GetValue())).Single();
            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator
                .ResolveAbility(offer, new[] { (IUnit)friend.GetValue() });

            RuleOperation.InvokeActivateUnitNext grant =
                ops.OfType<RuleOperation.InvokeActivateUnitNext>().Single();
            Assert.That(grant.Target, Is.SameAs(friend.GetValue()),
                "the grant names the picked unit, not the bearer - this is not a self-reactivation");
        }

        // "May not be used if this unit was activated via Coordinate" is authored as a condition over the
        // marker the consumer stamps, so the engine enforces the anti-chain without knowing the rule.
        [Test]
        public void Dispatch_AUnitThatItselfActivatedOutOfOrder_IsNotOfferedTheAbility()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> bearer = MakeUnit(_player, "General", new Position(5f, 5f), ActivateNextRule());

            bearer.GetValue().Tokens.AddToken(
                TokenDefinitionCatalog.Create(TokenType.ActivatedOutOfOrder));

            Assert.That(ctx.RuleEvaluator.GatherOffers(new ActivationEndContext(bearer.GetValue())), Is.Empty,
                "the chain stops after one link - otherwise a line of bearers activates the whole army");
        }

        // ── The activation-state fact the ally case needs ────────────────────────────────────────────────

        [Test]
        public void ActivatedThisRound_TracksThePoolInLockstep()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(_player, "Infantry", new Position(5f, 5f));
            SingleRoundContext round = MakeRound(ctx, _player);

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.ActivatedThisRound), Is.False);

            round.MarkUnitAsActivated(unit);
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.ActivatedThisRound), Is.True,
                "leaving the pool is what 'has activated' means");

            round.ReinstateUnitForActivation(unit);
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.ActivatedThisRound), Is.False,
                "a reactivated unit (Martial Prowess) has not activated again yet - the two must not drift");
        }

        // ── Producer: the end-of-activation offer ────────────────────────────────────────────────────────

        [Test]
        public async Task Producer_PickingAFriendlyUnit_FlagsItToActivateNext()
        {
            var requester = new ActivateNextRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> bearer = MakeUnit(_player, "General", new Position(5f, 5f), ActivateNextRule());
            DataBinding<UnitData> friend = MakeUnit(_player, "Infantry", new Position(9f, 5f));

            await RunEndOfActivation(ctx, bearer);

            Assert.That(friend.GetValue().Tokens.HasToken(TokenType.ActivatesNext), Is.True,
                "the picked unit is flagged for the next activation");
            Assert.That(bearer.GetValue().Tokens.HasToken(TokenType.ActivatesNext), Is.False);
        }

        [Test]
        public async Task Producer_TheBearerIsNeverACandidate()
        {
            var requester = new ActivateNextRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> bearer = MakeUnit(_player, "General", new Position(5f, 5f), ActivateNextRule());
            MakeUnit(_player, "Infantry", new Position(9f, 5f));

            await RunEndOfActivation(ctx, bearer);

            Assert.That(requester.OfferedLabels, Does.Not.Contain("General"),
                "'ANOTHER friendly unit' - a Friend selector includes the bearer, so it is filtered out");
        }

        [Test]
        public async Task Producer_AUnitThatHasAlreadyActivated_IsNotACandidate()
        {
            var requester = new ActivateNextRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> bearer = MakeUnit(_player, "General", new Position(5f, 5f), ActivateNextRule());
            DataBinding<UnitData> spent = MakeUnit(_player, "Veterans", new Position(9f, 5f));
            SingleRoundContext round = MakeRound(ctx, _player);
            round.MarkUnitAsActivated(spent);

            await RunEndOfActivation(ctx, bearer);

            Assert.That(requester.SelectionAsked, Is.False,
                "'that hasn't activated yet' - with no eligible unit the ability is not offered at all, " +
                "rather than offered and then found to have nothing to do");
            Assert.That(spent.GetValue().Tokens.HasToken(TokenType.ActivatesNext), Is.False);
        }

        [Test]
        public async Task Producer_AUnitOutOfRange_IsNotACandidate()
        {
            var requester = new ActivateNextRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> bearer = MakeUnit(_player, "General", new Position(5f, 5f), ActivateNextRule());
            DataBinding<UnitData> far = MakeUnit(_player, "Infantry", new Position(45f, 5f));

            await RunEndOfActivation(ctx, bearer);

            Assert.That(requester.SelectionAsked, Is.False, "the selector's 12in is enforced");
            Assert.That(far.GetValue().Tokens.HasToken(TokenType.ActivatesNext), Is.False);
        }

        [Test]
        public async Task Producer_Cancelling_FlagsNothing()
        {
            var requester = new ActivateNextRequester { Cancel = true };
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> bearer = MakeUnit(_player, "General", new Position(5f, 5f), ActivateNextRule());
            DataBinding<UnitData> friend = MakeUnit(_player, "Infantry", new Position(9f, 5f));

            await RunEndOfActivation(ctx, bearer);

            Assert.That(requester.SelectionAsked, Is.True);
            Assert.That(friend.GetValue().Tokens.HasToken(TokenType.ActivatesNext), Is.False,
                "the grant is resolved only after the pick, so cancelling costs nothing");
        }

        [Test]
        public async Task Producer_AnAlliedUnit_IsACandidateAndTheBeatSaysWhoControlsIt()
        {
            var sink = new RecordingPresentationSink();
            var requester = new ActivateNextRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, presentationSink: sink);
            DataBinding<UnitData> bearer = MakeUnit(_player, "General", new Position(5f, 5f), ActivateNextRule());
            DataBinding<UnitData> allyUnit = MakeUnit(_ally, "Allied Armor", new Position(9f, 5f));
            MakeTeam(1, _player, _ally);

            await RunEndOfActivation(ctx, bearer);

            Assert.That(allyUnit.GetValue().Tokens.HasToken(TokenType.ActivatesNext), Is.True,
                "'another FRIENDLY unit' includes a teammate's (owner ruling 2026-07-30)");
            Assert.That(sink.Beats.Any(b => b.Text != null && b.Text.Contains("activates next")), Is.True,
                "the turn order is about to skip a player - that gets a banner");
        }

        // ── Consumer: the cursor ─────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Consumer_TheCursorStaysWithTheOwner_InsteadOfAlternating()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mine = MakeUnit(_player, "Infantry", new Position(5f, 5f));
            MakeUnit(_enemy, "Enemy", new Position(25f, 5f));
            SingleRoundContext round = MakeTwoTeamRound(ctx);

            mine.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatesNext));

            await RunTurnStage(ctx, round);

            Assert.That(round.GetCurrentPlayerID(), Is.EqualTo(_player),
                "the flag displaces the alternation - without it the cursor would move to the enemy team");
        }

        [Test]
        public async Task Consumer_AnAllysFlaggedUnit_MovesTheCursorToTheAlly()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeUnit(_player, "Infantry", new Position(5f, 5f));
            DataBinding<UnitData> allyUnit = MakeUnit(_ally, "Allied Armor", new Position(9f, 5f));
            MakeUnit(_enemy, "Enemy", new Position(25f, 5f));
            var myTeam = MakeTeam(1, _player, _ally);
            MakeTeam(2, _enemy);
            SingleRoundContext round = new SingleRoundContext(ctx,
                new List<ITeam> { myTeam, TeamOf(_enemy) });

            allyUnit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatesNext));

            await RunTurnStage(ctx, round);

            Assert.That(round.GetCurrentPlayerID(), Is.EqualTo(_ally),
                "the ally resolves the activation with their own controller - the granting player does not " +
                "drive someone else's unit");
        }

        [Test]
        public async Task Consumer_AFlagOnAnAlreadyActivatedUnit_IsIgnored()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> spent = MakeUnit(_player, "Infantry", new Position(5f, 5f));
            MakeUnit(_enemy, "Enemy", new Position(25f, 5f));
            SingleRoundContext round = MakeTwoTeamRound(ctx);

            round.MarkUnitAsActivated(spent);
            spent.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatesNext));

            await RunTurnStage(ctx, round);

            Assert.That(round.GetCurrentPlayerID(), Is.EqualTo(_enemy),
                "the POOL is authoritative: a stale flag degrades to a normal advance rather than pinning " +
                "the round on a unit that can never be picked");
        }

        // The other half of "the pool is authoritative". The test above covers a unit that LEFT the pool;
        // this one covers a unit still in it that can no longer activate, which is the case a flag can
        // reach by simply outliving its target (granted, then the unit is wiped before its turn).
        [Test]
        public async Task Consumer_AFlagOnADeadUnit_IsIgnored()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> doomed = MakeUnit(_player, "Infantry", new Position(5f, 5f));
            MakeUnit(_player, "Veterans", new Position(9f, 5f));
            MakeUnit(_enemy, "Enemy", new Position(25f, 5f));
            SingleRoundContext round = MakeTwoTeamRound(ctx);

            doomed.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatesNext));
            foreach (IModel model in doomed.GetValue().Models)
            {
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
            }

            await RunTurnStage(ctx, round);

            Assert.That(round.GetCurrentPlayerID(), Is.EqualTo(_enemy),
                "a flag whose target died before its turn must not pin the round on a corpse");
        }

        // ── Consumer: the unit pick ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Consumer_TheFlaggedUnitActivatesWithNoMenu_AndIsMarkedOutOfOrder()
        {
            var requester = new ActivateNextRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> flagged = MakeUnit(_player, "Infantry", new Position(5f, 5f));
            DataBinding<UnitData> other = MakeUnit(_player, "Veterans", new Position(9f, 5f));

            flagged.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatesNext));

            var turn = new SingleTurnContext(ctx, _player,
                new List<DataBinding<UnitData>> { flagged, other });
            var stage = new ChooseUnitToActivateStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.ToMainUnitAction.Bind("activate");
            stage.ToDelayedTurnEnd.Bind("delayed");
            await stage.Enter(turn);

            Assert.That(requester.UnitMenuAsked, Is.False,
                "the choice was already made by whatever granted the activation - asking again would let " +
                "the player activate a different unit and strand the flag");
            Assert.That(turn.ActivatedUnit, Is.EqualTo(flagged));
            Assert.That(flagged.GetValue().Tokens.HasToken(TokenType.ActivatesNext), Is.False,
                "the flag is consumed by the activation it caused");
            Assert.That(flagged.GetValue().Tokens.HasToken(TokenType.ActivatedOutOfOrder), Is.True,
                "and records HOW it activated, which is what the anti-chain condition reads");
        }

        [Test]
        public async Task Consumer_WithNoFlag_TheOrdinaryMenuStillRuns()
        {
            var requester = new ActivateNextRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> a = MakeUnit(_player, "Infantry", new Position(5f, 5f));
            DataBinding<UnitData> b = MakeUnit(_player, "Veterans", new Position(9f, 5f));

            var turn = new SingleTurnContext(ctx, _player, new List<DataBinding<UnitData>> { a, b });
            var stage = new ChooseUnitToActivateStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.ToMainUnitAction.Bind("activate");
            stage.ToDelayedTurnEnd.Bind("delayed");
            await stage.Enter(turn);

            Assert.That(requester.UnitMenuAsked, Is.True,
                "the override must not leak into ordinary activations");
        }

        // ── Fixture ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The shape the supplement authors Coordinate in: a free end-of-activation ability targeting one
        /// friendly unit within 12in, blocked when the bearer itself activated out of order.
        /// </summary>
        private static SpecialRuleDefinition ActivateNextRule() => new SpecialRuleDefinition("Activate Next",
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnEndOfActivation, new Cost.Free(),
                    new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                    new Effect.ActivateUnitNext(),
                    new Condition.Not(new Condition.TokenPresent(TokenType.ActivatedOutOfOrder))),
            },
            ERuleScope.Unit);

        private async Task RunEndOfActivation(TriggeredMoveTestContext ctx, DataBinding<UnitData> bearer)
        {
            var turn = new SingleTurnContext(ctx, bearer.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { bearer });
            turn.ChooseUnitToActivate(bearer);

            var stage = new ReconcileEndOfActivationStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(turn);
        }

        private static async Task RunTurnStage(TriggeredMoveTestContext ctx, SingleRoundContext round)
        {
            var stage = new DeterminePlayerTurnStage(ctx, new NoOpLayer<ISingleRoundContext>());
            stage.OnDeterminedPlayerTurn.Bind("done");
            stage.OnNoPlayersLeft.Bind("done");
            await stage.Enter(round);
        }

        private SingleRoundContext MakeRound(TriggeredMoveTestContext ctx, PlayerID player)
        {
            ITeam team = MakeTeam(1, player);
            return new SingleRoundContext(ctx, new List<ITeam> { team });
        }

        // Two teams so the cursor has somewhere else to go - the whole point of the override tests.
        private SingleRoundContext MakeTwoTeamRound(TriggeredMoveTestContext ctx)
        {
            ITeam mine = MakeTeam(1, _player);
            ITeam theirs = MakeTeam(2, _enemy);
            return new SingleRoundContext(ctx, new List<ITeam> { mine, theirs });
        }

        private ITeam MakeTeam(int number, params PlayerID[] players)
        {
            var team = new TeamData(number, players.ToList());
            _store.Create(team); // surfaces in TableState.Teams, which SingleRoundContext reads
            return team;
        }

        private ITeam TeamOf(PlayerID player) =>
            _store.GetAllValues<TeamData>().First(team => team.IsPlayerOnTeam(player));

        // NOTE: every fixture position is off the origin on purpose. A unit whose living models all sit at
        // exactly (0,0) reads as off-battlefield (IUnit.GetIsOnBattlefield), so it never enters the round's
        // activation pool and every pool assertion below would fail for the wrong reason.
        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Position position,
            SpecialRuleDefinition? rule = null)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), position, _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (rule != null)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Answers the target pick (first option, or cancel) and records what was offered; answers the unit menu
    // if the stage ever reaches it, which several tests assert it does NOT.
    internal sealed class ActivateNextRequester : IPlayerRequestByID
    {
        public bool Cancel { get; init; }

        public bool SelectionAsked { get; private set; }
        public bool UnitMenuAsked { get; private set; }
        public List<string> OfferedLabels { get; } = new List<string>();

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case CancellableSelectionRequest<UnitData> selection:
                    SelectionAsked = true;
                    OfferedLabels.AddRange(selection.ValidOptions.Select(o => o.Name));
                    CancellableResult<DataBinding<UnitData>> reply = Cancel
                        ? new Cancelled<DataBinding<UnitData>>()
                        : new Selected<DataBinding<UnitData>>(selection.ValidOptions[0].Option);
                    return Task.FromResult((TReply)(object)reply);

                case ChooseUnitToActivateRequest unitMenu:
                    UnitMenuAsked = true;
                    return Task.FromResult((TReply)(object)unitMenu.ValidOptions[0].Option);

                case YesNoRequest:
                    return Task.FromResult((TReply)(object)true);

                default:
                    throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
