using FDG.Data;
using FDG.Players;
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
    // Vertical-slice integration test for #042 Phase 7h (reactivation primitive): proves Martial Prowess
    // offers an already-activated unit a second activation through the real DeterminePlayerTurnStage.
    //  - Dispatch: the catalog rule queues InvokeReactivate + a once-per-game cost marker, and stops
    //    offering once the marker is present (the cost gate the imperative-op executor never enforces).
    //  - Pool mechanics: ReinstateUnitForActivation returns the unit to the round's unactivated pool so it
    //    can be marked activated again without throwing.
    //  - Stage: accepting the offer re-adds the unit to the pool and grants the marker; declining does not.
    [TestFixture]
    public class ReactivateRuleIntegrationTests
    {
        private static readonly TokenType UsedMarker = new("AbilityUsed:Martial Prowess");

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public void Dispatch_OffersReactivation_AndQueuesCostMarker()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> bearer = MakeUnit("Champions", withMartialProwess: true, new Position(5f, 5f));

            var hookContext = new NextActivatorRequestedContext(bearer.GetValue());
            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(hookContext);

            Assert.That(offers.Count, Is.EqualTo(1), "an unused Martial Prowess is offered at the next-activator hook");

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offers[0],
                new[] { (IUnit)bearer.GetValue() });

            Assert.That(ops.OfType<RuleOperation.InvokeReactivate>().Any(op => op.Unit == bearer.GetValue()), Is.True,
                "accepting queues an InvokeReactivate for the bearer");
            Assert.That(ops.OfType<RuleOperation.GrantTokenToUnit>().Any(op => op.TokenToGrant.Type == UsedMarker), Is.True,
                "the once-per-game cost marker is queued");
        }

        [Test]
        public void Dispatch_OncePerGame_NotOfferedAfterMarkerPresent()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> bearer = MakeUnit("Champions", withMartialProwess: true, new Position(5f, 5f));

            bearer.GetValue().Tokens.AddToken(new Token(UsedMarker, 1, new TokenClearTrigger.ManualOnly()));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new NextActivatorRequestedContext(bearer.GetValue()));

            Assert.That(offers, Is.Empty, "with the used-marker present the once-per-game gate is closed");
        }

        [Test]
        public void Reinstate_ReturnsUnitToPool_SoItCanActivateAgain()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> bearer = MakeUnit("Champions", withMartialProwess: true, new Position(5f, 5f));
            SingleRoundContext round = MakeRound(ctx);

            round.MarkUnitAsActivated(bearer);
            Assert.That(round.UnactivatedUnits[_player], Does.Not.Contain(bearer), "marking activated removes it from the pool");

            round.ReinstateUnitForActivation(bearer);
            Assert.That(round.UnactivatedUnits[_player], Does.Contain(bearer), "reinstating returns it to the pool");

            // The payoff: a reinstated unit can be marked activated again without the not-in-pool throw.
            Assert.DoesNotThrow(() => round.MarkUnitAsActivated(bearer));
        }

        [Test]
        public async Task Stage_Accept_ReinstatesBearerAndGrantsMarker()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: true));
            DataBinding<UnitData> bearer = MakeUnit("Champions", withMartialProwess: true, new Position(5f, 5f));
            DataBinding<UnitData> other = MakeUnit("Warriors", withMartialProwess: false, new Position(7f, 5f));
            SingleRoundContext round = MakeRound(ctx);

            // Bearer has already activated this round; the other unit keeps the player's turn alive.
            round.MarkUnitAsActivated(bearer);

            await RunStage(ctx, round);

            Assert.That(round.UnactivatedUnits[_player], Does.Contain(bearer),
                "accepting Martial Prowess returns the bearer to the pool");
            Assert.That(bearer.GetValue().Tokens.HasToken(UsedMarker), Is.True,
                "the once-per-game marker is granted so it can't reactivate again");
        }

        [Test]
        public async Task Stage_Decline_DoesNotReinstate()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedYesNoRequester(accept: false));
            DataBinding<UnitData> bearer = MakeUnit("Champions", withMartialProwess: true, new Position(5f, 5f));
            DataBinding<UnitData> other = MakeUnit("Warriors", withMartialProwess: false, new Position(7f, 5f));
            SingleRoundContext round = MakeRound(ctx);

            round.MarkUnitAsActivated(bearer);

            await RunStage(ctx, round);

            Assert.That(round.UnactivatedUnits[_player], Does.Not.Contain(bearer),
                "declining leaves the bearer out of the pool");
            Assert.That(bearer.GetValue().Tokens.HasToken(UsedMarker), Is.False,
                "declining spends nothing");
        }

        private static async Task RunStage(TriggeredMoveTestContext ctx, SingleRoundContext round)
        {
            var stage = new DeterminePlayerTurnStage(ctx, new NoOpLayer<ISingleRoundContext>());
            stage.OnDeterminedPlayerTurn.Bind("done");
            stage.OnNoPlayersLeft.Bind("done");
            await stage.Enter(round);
        }

        private SingleRoundContext MakeRound(TriggeredMoveTestContext ctx)
        {
            var team = new TeamData(1, new List<PlayerID> { _player });
            _store.Create(team); // surfaces in TableState.Teams, which SingleRoundContext reads
            return new SingleRoundContext(ctx, new List<ITeam> { team });
        }

        private DataBinding<UnitData> MakeUnit(string name, bool withMartialProwess, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new List<SpecialRule>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(), modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (withMartialProwess)
            {
                binding.GetValue().AttachRuleDefinition(
                    new ResolvedRule("Martial Prowess", CoreRuleCatalog.MartialProwess));
            }

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Answers every YesNo offer with a fixed choice. DeterminePlayerTurnStage issues only YesNoRequests.
    internal sealed class CannedYesNoRequester : IPlayerRequestByID
    {
        private readonly bool _accept;

        public CannedYesNoRequester(bool accept) => _accept = accept;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is YesNoRequest)
            {
                return Task.FromResult((TReply)(object)_accept);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
