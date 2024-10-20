
namespace FDG.Stages
{

    public class MainUnitActionStage : StateBase<IPlayerTurnContext>
    {
        public const string MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION =
            "MainUnitActionToChildChooseAction";

        public MainUnitActionStage(StateMachine stateMachine, IPlayerTurnContext context, 
            IUnitActionContext mainUnitActionContext, IMeleeContext meleeContext,
            IRangedContext rangedContext, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            ChooseActionStage chooseActionStage = new ChooseActionStage(stateMachine, mainUnitActionContext, this);
            MovementStage movementStage = new MovementStage(stateMachine, mainUnitActionContext, this);
            MeleeStage meleeStage = new MeleeStage(stateMachine, mainUnitActionContext, meleeContext, this);
            ShootStage shootStage = new ShootStage(stateMachine, mainUnitActionContext, rangedContext, this);

            stateMachine.AddTransition<MainUnitActionStage>(MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION,
                chooseActionStage);
            stateMachine.AddTransition<ChooseActionStage>(ChooseActionStage.CHOOSE_ACTION_TO_MOVEMENT_TRANSITION,
                movementStage);
            stateMachine.AddTransition<MovementStage>(MovementStage.MOVEMENT_TO_MELEE_TRANSITION, meleeStage);
            stateMachine.AddTransition<MovementStage>(MovementStage.MOVEMENT_TO_RANGED_TRANSITION, shootStage);

            //TODO: Bind the above to leave this stage.
        }


        public override void Enter()
        {
            base.Enter();

            Context.Log("Main Unit Action stage entering child: Choose Action stage.");
            MoveToChildChooseActivation();
        }

        private void MoveToChildChooseActivation()
        {
            SignalEvent(MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION);
        }

    }
}