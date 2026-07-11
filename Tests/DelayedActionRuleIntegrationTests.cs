using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 Delayed Action: "once per round, if your opponent has more units left to activate than you, this
    // unit may pass its turn instead of activating (may still be activated later)." A marker rule detected by
    // ChooseUnitToActivateStage: after the player picks the unit, if the gate holds and the team hasn't
    // already held back this round, it offers a Yes/No hold-back. Accepting passes the turn (the unit stays
    // in the pool, SingleTurnStage skips MarkUnitAsActivated) so the cursor advances to the opponent.
    [TestFixture]
    public class DelayedActionRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _me;
        private PlayerID _opponent;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _me = new PlayerID(Guid.NewGuid());
            _opponent = new PlayerID(Guid.NewGuid());
            // Two teams surface in TableState.Teams, which the stage's team-scan reads.
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _opponent }));
        }

        [Test]
        public void Catalog_ResolvesDelayedAction_AsAMarker()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();

            Assert.That(resolver.TryResolve(CoreRuleCatalog.DelayedActionRuleName, out ResolvedRule resolved), Is.True);
            Assert.That(resolved.Definition.Passive, Is.Empty, "Delayed Action is a marker - no dispatch hooks.");
            Assert.That(resolved.Definition.Activated, Is.Empty, "Delayed Action is a marker - no abilities.");
        }

        [Test]
        public async Task Eligible_AndAccepted_HoldsBack_DoesNotActivate_StaysInPool()
        {
            DataBinding<UnitData> holder = MakeUnit(_me, "Holders", withDelayedAction: true);
            var requester = new ActivationChoiceRequester(holder, delay: true);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            var turn = new SingleTurnContext(ctx, _me, new List<DataBinding<UnitData>> { holder },
                opponentHasMoreUnitsToActivate: true);

            (bool toDelay, bool toMain) = await RunStage(ctx, turn);

            Assert.That(requester.YesNoAsked, Is.True, "an eligible unit is offered the hold-back.");
            Assert.That(toDelay, Is.True, "accepting routes to the delayed-turn end.");
            Assert.That(toMain, Is.False, "accepting does NOT activate the unit.");
            Assert.That(turn.WasDelayed, Is.True);
            Assert.That(turn.ActivatedUnit, Is.Null, "no unit was activated this turn.");
            Assert.That(holder.GetValue().Tokens.HasToken(TokenType.DelayedActionUsed), Is.True,
                "the once-per-round-per-team marker is placed.");
        }

        [Test]
        public async Task Eligible_ButDeclined_ActivatesNormally()
        {
            DataBinding<UnitData> holder = MakeUnit(_me, "Holders", withDelayedAction: true);
            var requester = new ActivationChoiceRequester(holder, delay: false);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            var turn = new SingleTurnContext(ctx, _me, new List<DataBinding<UnitData>> { holder },
                opponentHasMoreUnitsToActivate: true);

            (bool toDelay, bool toMain) = await RunStage(ctx, turn);

            Assert.That(requester.YesNoAsked, Is.True, "the offer was made...");
            Assert.That(toMain, Is.True, "...but declining activates the unit normally.");
            Assert.That(toDelay, Is.False);
            Assert.That(turn.WasDelayed, Is.False);
            Assert.That(turn.ActivatedUnit, Is.EqualTo(holder));
            Assert.That(holder.GetValue().Tokens.HasToken(TokenType.DelayedActionUsed), Is.False,
                "declining spends nothing.");
        }

        [Test]
        public async Task NoOffer_WhenOpponentDoesNotHaveMoreUnitsLeft()
        {
            DataBinding<UnitData> holder = MakeUnit(_me, "Holders", withDelayedAction: true);
            var requester = new ActivationChoiceRequester(holder, delay: true);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            var turn = new SingleTurnContext(ctx, _me, new List<DataBinding<UnitData>> { holder },
                opponentHasMoreUnitsToActivate: false); // gate open only when outnumbered in activations

            (bool toDelay, bool toMain) = await RunStage(ctx, turn);

            Assert.That(requester.YesNoAsked, Is.False, "no hold-back is offered when not outnumbered.");
            Assert.That(toMain, Is.True);
            Assert.That(toDelay, Is.False);
        }

        [Test]
        public async Task NoOffer_WhenUnitLacksTheRule()
        {
            DataBinding<UnitData> plain = MakeUnit(_me, "Line", withDelayedAction: false);
            var requester = new ActivationChoiceRequester(plain, delay: true);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            var turn = new SingleTurnContext(ctx, _me, new List<DataBinding<UnitData>> { plain },
                opponentHasMoreUnitsToActivate: true);

            (bool toDelay, bool toMain) = await RunStage(ctx, turn);

            Assert.That(requester.YesNoAsked, Is.False, "a unit without Delayed Action is never offered it.");
            Assert.That(toMain, Is.True);
        }

        [Test]
        public async Task NoOffer_WhenTeamAlreadyHeldBackThisRound()
        {
            // A teammate already used the once-per-round hold-back (carries the marker).
            DataBinding<UnitData> alreadyUsed = MakeUnit(_me, "First", withDelayedAction: true);
            alreadyUsed.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.DelayedActionUsed));

            DataBinding<UnitData> holder = MakeUnit(_me, "Second", withDelayedAction: true);
            var requester = new ActivationChoiceRequester(holder, delay: true);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            var turn = new SingleTurnContext(ctx, _me,
                new List<DataBinding<UnitData>> { alreadyUsed, holder },
                opponentHasMoreUnitsToActivate: true);

            (bool toDelay, bool toMain) = await RunStage(ctx, turn);

            Assert.That(requester.YesNoAsked, Is.False, "the team's one hold-back per round is already spent.");
            Assert.That(toMain, Is.True);
        }

        // Runs ChooseUnitToActivateStage and reports which edge fired.
        private static async Task<(bool toDelay, bool toMain)> RunStage(
            TriggeredMoveTestContext ctx, SingleTurnContext turn)
        {
            bool toDelay = false, toMain = false;
            var stage = new ChooseUnitToActivateStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.ToDelayedTurnEnd.Bind("ToDelayedTurnEnd");
            stage.ToDelayedTurnEnd.OnWillActivate += _ => toDelay = true;
            stage.ToMainUnitAction.Bind("ToMainUnitAction");
            stage.ToMainUnitAction.OnWillActivate += _ => toMain = true;

            await stage.Enter(turn);
            return (toDelay, toMain);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, bool withDelayedAction)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(5f, 5f), _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(owner, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (withDelayedAction)
            {
                binding.GetValue().AttachRuleDefinition(
                    new ResolvedRule(CoreRuleCatalog.DelayedActionRuleName, CoreRuleCatalog.DelayedAction));
            }

            // Merge into this owner's single army (the stage lists units from armies it owns).
            ArmyData? army = _store.GetAllValues<ArmyData>().FirstOrDefault(a => a.PlayerID == owner);
            if (army == null)
            {
                _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            }
            else
            {
                army.UnitBindings.Add(binding);
            }

            return binding;
        }
    }

    // Answers the unit pick with a fixed unit and the Delayed Action Yes/No with a fixed choice, recording
    // whether the hold-back was ever offered.
    internal sealed class ActivationChoiceRequester : IPlayerRequestByID
    {
        private readonly DataBinding<UnitData> _pick;
        private readonly bool _delay;

        public bool YesNoAsked { get; private set; }

        public ActivationChoiceRequester(DataBinding<UnitData> pick, bool delay)
        {
            _pick = pick;
            _delay = delay;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is ChooseUnitToActivateRequest)
                return Task.FromResult((TReply)(object)_pick);
            if (request is YesNoRequest)
            {
                YesNoAsked = true;
                return Task.FromResult((TReply)(object)_delay);
            }
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
