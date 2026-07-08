using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    // #100 — RestrictActions / Immobile. A passive rule at Activation_OnActionChoice restricts which action
    // types the unit may declare; ChooseActionStage reads it and grays out the disallowed menu options.
    [TestFixture]
    public class RestrictActionsRuleIntegrationTests
    {
        // Eval layer: the Immobile rule produces a RestrictActions([Hold]) op at the action-choice hook.
        [Test]
        public void Immobile_AtActionChoice_RestrictsToHold()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Immobile);
            IUnit unit = harness.BuildUnit("P1", modelCount: 1, "Immobile");

            var ops = harness.EvaluateAll(new ActionChoiceContext(unit), RuleParticipant.Actor(unit));

            ops.HasOperation<RuleOperation.RestrictActions>(op =>
                op.Allowed.Count == 1 && op.Allowed[0] == EActionType.Hold);
        }

        // Stage layer: ChooseActionStage grays out Move for an Immobile unit. A custom action keeps the menu
        // from short-circuiting (an Immobile unit with nothing else valid would just pass without prompting).
        [Test]
        public async Task ChooseAction_ImmobileUnit_GraysOutMoveWithReason()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> unit = MakeImmobileUnitWithCustomAction(store, player);

            var capturer = new CapturingStringSelectionRequester("Pass");
            var ctx = new TriggeredMoveTestContext(store, capturer);
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToMovement.Bind("Move");
            stage.ToCharge.Bind("Charge");
            stage.ToShoot.Bind("Shoot");
            stage.ToCustomAction.Bind("Custom");
            stage.ToDisembark.Bind("Disembark");
            stage.ToEmbark.Bind("Embark");
            stage.ToReconcileEndOfActivation.Bind("Done");
            await stage.Enter(unitCtx);

            StringSelectionRequest req = capturer.Captured!;
            Assert.That(req.ValidOptions, Does.Not.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "Immobile removes Move from the offered actions");
            Assert.That(req.InvalidOptions.Any(o =>
                    o.Option == ChooseActionStage.MOVEMENT_CHOICE_NAME && o.Reason == "Immobile."),
                Is.True, "Move is shown disabled with the Immobile reason");
        }

        // --- Helpers ---

        private static DataBinding<UnitData> MakeImmobileUnitWithCustomAction(GameDataStore store, PlayerID player)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(1f, 0f), store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            };
            var unit = new UnitData(player, "Immobile Unit", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = store.GetDataBinding<UnitData>(store.Create(unit));

            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Immobile", CoreRuleCatalog.Immobile));

            // A trivial custom action so ChooseAction issues its menu instead of short-circuiting to Pass.
            var ability = new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.GrantToken(new TokenType("CustomFired"), new ValueSource.Literal(1),
                    new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            var customDef = new SpecialRuleDefinition("Test Cast", Array.Empty<HookEntry>(), new[] { ability });
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Test Cast", customDef));

            store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Captures the StringSelectionRequest the stage emits and answers it with a fixed choice.
    internal sealed class CapturingStringSelectionRequester : IPlayerRequestByID
    {
        private readonly string _choice;
        public StringSelectionRequest? Captured { get; private set; }

        public CapturingStringSelectionRequester(string choice) => _choice = choice;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is StringSelectionRequest ssr)
            {
                Captured = ssr;
                return Task.FromResult((TReply)(object)_choice);
            }
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
