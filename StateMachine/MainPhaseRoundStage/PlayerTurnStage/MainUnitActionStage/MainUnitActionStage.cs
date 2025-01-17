
using System.Collections.Generic;

namespace FDG.Stages
{

    public class MainUnitActionStage : ParentStage<IPlayerTurnContext, IUnitActionContext>
    {
        public const string MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION =
            "MainUnitActionToChildChooseAction";

        public StageBinding ToReconcileEndOfActivation;

        private readonly ChooseActionStage _chooseActionStage;
        private readonly MovementStage _movementStage;

        private readonly MeleeStage _meleeStage;

        private readonly ShootStage _shootStage;

        public MainUnitActionStage(IGameContext gameContext, IStateMachineLayer<IPlayerTurnContext> parent) : base(gameContext, parent)
        {
            
        }

        public override void Enter(IPlayerTurnContext context)
        {
            GameContext.Log("Main Unit Action stage entered.");

            base.Enter(context);
        }

        protected override IUnitActionContext GetNewChildContext(IPlayerTurnContext contextSelf)
        {
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

            return dictionary;
        }
    }
}