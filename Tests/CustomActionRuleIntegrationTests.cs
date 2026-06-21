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
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #010 — Custom actions branch in ChooseActionStage. A special rule carrying an ActivatedAbility
    // triggered at Activation_OnActionChoice surfaces as a selectable action; choosing it resolves the
    // ability generically and loops back to Choose Action WITHOUT consuming the move/shoot (layered).
    // This is the generic seam, proved with a trivial self-buff "Cast" rule — NOT the Caster subsystem.
    [TestFixture]
    public class CustomActionRuleIntegrationTests
    {
        private static readonly TokenType UsedMarker = new("AbilityUsed:Cast");

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
        }

        // --- Eval layer: the new ActionChoiceContext drives the existing offer machinery ---

        [Test]
        public void GatherOffers_AtActionChoiceHook_OffersTheCustomAction()
        {
            var harness = new TestRuleHarness();
            (SpecialRuleDefinition def, _, _) = MakeCastRule();
            harness.Register(def);
            IUnit caster = harness.BuildUnit("P1", modelCount: 1, "Cast");

            var offers = harness.OfferAbilities(new ActionChoiceContext(caster));

            offers.HasOffer("Cast");
        }

        [Test]
        public void GatherOffers_OncePerActivationUsed_NoLongerOffered()
        {
            var harness = new TestRuleHarness();
            (SpecialRuleDefinition def, _, _) = MakeCastRule();
            harness.Register(def);
            IUnit caster = harness.BuildUnit("P1", modelCount: 1, "Cast");
            caster.Tokens.AddToken(new Token(UsedMarker, 1, new TokenClearTrigger.ActivationEnd()));

            var offers = harness.OfferAbilities(new ActionChoiceContext(caster));

            Assert.That(offers, Is.Empty,
                "with the once-per-activation used marker present the custom action is no longer offered");
        }

        // --- Stage layer: ChooseActionStage surfaces + routes the custom action ---

        [Test]
        public async Task ChooseAction_WithCustomActionRule_SurfacesAndRoutesToCustomAction()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedStringChoiceRequester("Cast"));
            DataBinding<UnitData> unit = MakeUnit(withCast: true, new Position(0f, 0f));
            var unitCtx = NewActivation(ctx, unit);

            bool routed = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToCustomAction.Bind("ToCustomAction");
            stage.ToCustomAction.OnWillActivate += _ => routed = true;
            await stage.Enter(unitCtx);

            Assert.That(routed, Is.True, "choosing the custom action routes to the custom-action stage");
            Assert.That(unitCtx.PendingCustomAction, Is.Not.Null, "the chosen offer is handed to the stage");
            Assert.That(unitCtx.PendingCustomAction!.RuleName, Is.EqualTo("Cast"));
        }

        // --- Stage layer: CustomActionStage resolves the ability, pays cost, and is layered ---

        [Test]
        public async Task CustomActionStage_Resolves_PaysCostAppliesEffect_AndDoesNotConsumeAction()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            (_, _, TokenType marker) = MakeCastRule();
            DataBinding<UnitData> unit = MakeUnit(withCast: true, new Position(0f, 0f));
            var unitCtx = NewActivation(ctx, unit);

            AbilityOffer offer = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()))[0];
            unitCtx.SetPendingCustomAction(offer);

            bool finished = false;
            var stage = new CustomActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(unit.GetValue().Tokens.HasToken(marker), Is.True,
                "the ability's effect (a self-buff token) was applied to the bearer");
            Assert.That(unit.GetValue().Tokens.HasToken(UsedMarker), Is.True,
                "the once-per-activation cost marker was paid");
            Assert.That(finished, Is.True, "the custom action loops back via OnFinished");
            Assert.That(unitCtx.HasMoved, Is.False, "casting is layered — it does not consume the move");
            Assert.That(unitCtx.HasAttacked, Is.False, "casting is layered — it does not consume the attack");
            Assert.That(unitCtx.PendingCustomAction, Is.Null, "the pending offer is cleared after resolving");
        }

        // --- Helpers ---

        // A self-buff "Cast": offered at the action-choice hook, once per activation, granting the bearer
        // a marker token. Deliberately trivial — exercises the seam, not spell mechanics.
        private static (SpecialRuleDefinition def, ActivatedAbility ability, TokenType marker) MakeCastRule()
        {
            var marker = new TokenType("CustomActionFired");
            var ability = new ActivatedAbility(
                TriggerHook: EHookID.Activation_OnActionChoice,
                Cost: new Cost.OncePerActivation(),
                TargetSelector: new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                Effect: new Effect.GrantToken(marker, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                AvailableWhen: new Condition.Always());
            var def = new SpecialRuleDefinition("Cast", System.Array.Empty<HookEntry>(), new[] { ability });
            return (def, ability, marker);
        }

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private DataBinding<UnitData> MakeUnit(bool withCast, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Test Unit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (withCast)
            {
                (SpecialRuleDefinition def, _, _) = MakeCastRule();
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Cast", def));
            }

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Answers a StringSelectionRequest with a fixed choice (the custom action's name). ChooseActionStage's
    // only request in these tests is the action StringSelectionRequest.
    internal sealed class CannedStringChoiceRequester : IPlayerRequestByID
    {
        private readonly string _choice;

        public CannedStringChoiceRequester(string choice) => _choice = choice;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is StringSelectionRequest)
            {
                return Task.FromResult((TReply)(object)_choice);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
