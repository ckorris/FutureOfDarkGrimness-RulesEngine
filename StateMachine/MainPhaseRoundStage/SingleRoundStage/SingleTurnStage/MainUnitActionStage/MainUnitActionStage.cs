
using System.Collections.Generic;

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
                .AddSibling(nameof(ToReconcileEndOfActivation), ToReconcileEndOfActivation, out string toReconcileActivationEvent)
                .Build();

            startingChild = chooseAction;

            chooseAction.ToMovement.Bind(movement);
            chooseAction.ToCharge.Bind(melee);
            chooseAction.ToShoot.Bind(shoot);
            chooseAction.ToReconcileEndOfActivation.Bind(toReconcileActivationEvent);
            movement.OnFinishedMovement.Bind(chooseAction);
            melee.OnFinishedMelee.Bind(chooseAction);
            shoot.OnFinishedShooting.Bind(chooseAction);
            shoot.BackToChooseAction.Bind(chooseAction);

            return dictionary;
        }
    }
}