
using System.Collections.Generic;
using FDG.Rules.Definitions;

namespace FDG.Stages
{

    public class MainUnitActionStage : ParentStage<ISingleTurnContext, IUnitActionContext>
    {
        public const string MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION =
            "MainUnitActionToChildChooseAction";

        public StageBinding ToReconcileEndOfActivation;

        public MainUnitActionStage(IGameContext gameContext, IStateMachineLayer<ISingleTurnContext> parent) : base(gameContext, parent)
        {
            
        }

        public override async Task Enter(ISingleTurnContext context)
        {
            GameContext.Log("Main Unit Action stage entered.");

            await base.Enter(context);
        }

        protected override IUnitActionContext GetNewChildContext(ISingleTurnContext contextSelf)
        {
            if(contextSelf.ActivatedUnit == null)
            {
                throw new NullReferenceException($"{nameof(ISingleTurnContext.ActivatedUnit)} was null when creating child context in {nameof(MainUnitActionStage)}.");
            }

            UnitActionContext unitActionContext = new UnitActionContext(GameContext, contextSelf.ActivatedUnit);
            unitActionContext.Reset(contextSelf.ActivatedUnit);
            return unitActionContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IUnitActionContext> startingChild)
        {
            ToReconcileEndOfActivation = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseActionStage(GameContext, this), out var chooseAction)
                .AddChild(new MovementStage(GameContext, this), out var movement)
                .AddChild(new MeleeStage(GameContext, this), out var melee)
                .AddChild(new ShootStage(GameContext, this), out var shoot)
                // #100 #2 — a pre-attack stage sits on each attack edge, firing Activation_OnPreAttack
                // and offering pre-attack abilities before the real attack resolves. One per action type
                // so each reports the right kind to the hook (Charge exact; the shoot edge uses Hold —
                // there is no Shoot action type — which no corpus pre-attack ability gates on).
                .AddChild(new PreAttackStage(GameContext, this, EActionType.Charge), out var preAttackMelee)
                .AddChild(new PreAttackStage(GameContext, this, EActionType.Hold), out var preAttackShoot)
                .AddChild(new CustomActionStage(GameContext, this), out var customAction)
                .AddChild(new CastSpellStage(GameContext, this), out var castSpell)
                .AddChild(new DisembarkStage(GameContext, this), out var disembark)
                .AddChild(new EmbarkStage(GameContext, this), out var embark)
                .AddSibling(nameof(ToReconcileEndOfActivation), ToReconcileEndOfActivation, out string toReconcileActivationEvent)
                .Build();

            startingChild = chooseAction;

            chooseAction.ToMovement.Bind(movement);
            // #100 #2 — route the attack edges through the pre-attack stage, which hands off to the real
            // attack on finish. Layered (no HasMoved/HasAttacked), so the downstream attack is unchanged.
            chooseAction.ToCharge.Bind(preAttackMelee);
            chooseAction.ToShoot.Bind(preAttackShoot);
            preAttackMelee.OnFinished.Bind(melee);
            preAttackShoot.OnFinished.Bind(shoot);
            chooseAction.ToCustomAction.Bind(customAction);
            chooseAction.ToCast.Bind(castSpell);
            chooseAction.ToDisembark.Bind(disembark);
            chooseAction.ToEmbark.Bind(embark);
            chooseAction.ToReconcileEndOfActivation.Bind(toReconcileActivationEvent);
            movement.OnFinishedMovement.Bind(chooseAction);
            melee.OnFinishedMelee.Bind(chooseAction);
            shoot.OnFinishedShooting.Bind(chooseAction);
            shoot.BackToChooseAction.Bind(chooseAction);
            // #010 — a resolved custom action loops back to Choose Action (layered, doesn't end the turn).
            customAction.OnFinished.Bind(chooseAction);
            // #033 — casting also loops back to Choose Action, layered (doesn't set HasMoved/HasAttacked).
            castSpell.OnFinished.Bind(chooseAction);
            // #035 — after disembarking (Advance-equivalent), loop back so the unit may still Shoot.
            disembark.OnFinished.Bind(chooseAction);
            // #035 slice D — boarding a transport ends the activation (the unit is now inside); cancelling
            // the transport choice returns to the action menu.
            embark.OnEmbarked.Bind(toReconcileActivationEvent);
            embark.OnBackToChooseAction.Bind(chooseAction);

            return dictionary;
        }
    }
}