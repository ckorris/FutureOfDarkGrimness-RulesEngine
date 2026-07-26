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
    // The bug this fixes: "before attacking" abilities (Piercing Spotter, Regeneration Buff, Precision
    // Fighting Mark, ...) used to be offered only AFTER pressing Shoot/Charge, so a unit that couldn't attack
    // anything could never use them. They are now first-class Choose Action menu options (like Cast): offered
    // whenever the unit hasn't attacked, whether or not it can attack, and dropped once it has attacked or
    // when the ability has no eligible target.
    [TestFixture]
    public class BeforeAttackActionMenuTests
    {
        private const string BuffName = "Regeneration Buff";

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        // The core fix: a unit with NO weapons and no enemies on the table - it cannot Shoot or Charge -
        // is still offered its before-attack buff in Choose Action, and picking it routes to the resolver.
        [Test]
        public async Task Offered_AndRoutes_EvenWhenUnitCannotAttack()
        {
            var capture = new CapturingChoiceRequester(BuffName);
            var ctx = new TriggeredMoveTestContext(_store, capture);
            DataBinding<UnitData> unit = MakeBuffUnit();
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            bool routed = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToBeforeAttackAction.Bind("ToBeforeAttackAction");
            stage.ToBeforeAttackAction.OnWillActivate += _ => routed = true;
            await stage.Enter(unitCtx);

            Assert.That(capture.Request!.ValidOptions, Does.Contain(BuffName),
                "the before-attack buff is a valid menu action even though the unit can't attack");
            Assert.That(capture.Request!.InvalidOptions.Any(o => o.Option == ChooseActionStage.SHOOT_CHOICE_NAME),
                Is.True, "precondition: the unit genuinely cannot Shoot");
            Assert.That(capture.Request!.InvalidOptions.Any(o => o.Option == ChooseActionStage.CHARGE_CHOICE_NAME),
                Is.True, "precondition: the unit genuinely cannot Charge");
            Assert.That(routed, Is.True, "choosing the buff routes to the before-attack resolver");
            Assert.That(unitCtx.PendingCustomAction, Is.Not.Null, "the chosen offer is handed to the stage");
            Assert.That(unitCtx.PendingCustomAction!.RuleName, Is.EqualTo(BuffName));
        }

        // A Foe-targeting before-attack ability with no enemy on the table has no eligible target, so it is
        // NOT offered - the same "don't offer an action that can't do anything" gate Shoot/Cast use.
        [Test]
        public async Task NotOffered_WhenNoEligibleTarget()
        {
            var capture = new CapturingChoiceRequester(ChooseActionStage.MOVEMENT_CHOICE_NAME);
            var ctx = new TriggeredMoveTestContext(_store, capture);
            DataBinding<UnitData> unit = MakeFoeMarkUnit(); // mark ability, but no enemies exist
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToMovement.Bind("ToMovement");
            await stage.Enter(unitCtx);

            Assert.That(capture.Request!.ValidOptions, Does.Not.Contain("Foe Mark"),
                "a before-attack ability with no eligible target is not offered");
        }

        // "Before attacking" means the window closes once the unit attacks: after HasAttacked the buff is no
        // longer offered. A weaponless unit that has attacked has nothing left to do, so Choose Action
        // reconciles the activation WITHOUT ever routing to the before-attack resolver.
        [Test]
        public async Task NotOffered_AfterTheUnitHasAttacked()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeBuffUnit();
            UnitActionContext unitCtx = NewActivation(ctx, unit);
            unitCtx.RegisterAttackedFinished(); // the unit has attacked this activation

            bool routedBuff = false;
            bool reconciled = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToBeforeAttackAction.Bind("ToBeforeAttackAction");
            stage.ToBeforeAttackAction.OnWillActivate += _ => routedBuff = true;
            stage.ToReconcileEndOfActivation.Bind("ToReconcileEndOfActivation");
            stage.ToReconcileEndOfActivation.OnWillActivate += _ => reconciled = true;
            await stage.Enter(unitCtx);

            Assert.That(routedBuff, Is.False, "once the unit has attacked, the before-attack buff is not offered");
            Assert.That(reconciled, Is.True, "with only Pass left, the activation reconciles instead of prompting");
        }

        // --- Helpers ---

        // A lone unit (its own only friendly) carrying a Friend-target before-attack buff and NO weapons, so
        // it can't Shoot or Charge but can still buff itself. Mirrors the Leech Engine case from the report.
        private DataBinding<UnitData> MakeBuffUnit()
        {
            var ability = new ActivatedAbility(
                EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.GrantToken(new TokenType("RegenBuffFired"), new ValueSource.Literal(1),
                    new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            return MakeUnit(new SpecialRuleDefinition(BuffName, Array.Empty<HookEntry>(), new[] { ability }));
        }

        private DataBinding<UnitData> MakeFoeMarkUnit()
        {
            var ability = new ActivatedAbility(
                EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, false),
                new Effect.GrantToken(new TokenType("FoeMarkFired"), new ValueSource.Literal(1),
                    new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            return MakeUnit(new SpecialRuleDefinition("Foe Mark", Array.Empty<HookEntry>(), new[] { ability }));
        }

        private DataBinding<UnitData> MakeUnit(SpecialRuleDefinition rule)
        {
            // Off-origin so GetIsOnBattlefield sees the unit as placed (a unit at the origin reads as an
            // unplaced reserve, which is never an eligible target - even for its own Friend abilities).
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(1f, 0f), _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Test Unit", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }
    }

    // Records the Choose Action request so the test can inspect the offered options, then answers with a
    // fixed valid choice so the stage completes.
    internal sealed class CapturingChoiceRequester : IPlayerRequestByID
    {
        private readonly string _choice;
        public StringSelectionRequest? Request { get; private set; }

        public CapturingChoiceRequester(string choice) => _choice = choice;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is StringSelectionRequest ssr)
            {
                Request = ssr;
                return Task.FromResult((TReply)(object)_choice);
            }
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
