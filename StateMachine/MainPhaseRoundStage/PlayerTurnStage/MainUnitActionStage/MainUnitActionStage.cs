
namespace FDG.Stages
{

    public class MainUnitActionStage : StageBase<IPlayerTurnContext>
    {
        public const string MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION =
            "MainUnitActionToChildChooseAction";

        private readonly ChooseActionStage _chooseActionStage;
        private readonly MovementStage _movementStage;

        private readonly MeleeStage _meleeStage;

        private readonly ShootStage _shootStage;

        public MainUnitActionStage(StateMachine stateMachine, IPlayerTurnContext context,
            IUnitActionContext mainUnitActionContext, IMeleeContext meleeContext,
            IRangedContext rangedContext, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _chooseActionStage = new ChooseActionStage(stateMachine, mainUnitActionContext, this);
            _movementStage = new MovementStage(stateMachine, mainUnitActionContext, this);
            _meleeStage = new MeleeStage(stateMachine, mainUnitActionContext, meleeContext, this);
            _shootStage = new ShootStage(stateMachine, mainUnitActionContext, rangedContext, this);

            Bind(MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION, _chooseActionStage);
            _chooseActionStage.Bind(ChooseActionStage.CHOOSE_ACTION_TO_MOVEMENT_TRANSITION,
                _movementStage);
            _chooseActionStage.Bind(ChooseActionStage.CHOOSE_ACTION_TO_CHARGE_TRANSITION,
                _meleeStage);
            _chooseActionStage.Bind(ChooseActionStage.CHOOSE_ACTION_TO_SHOOT_TRANSITION,
                _shootStage);

            _movementStage.Bind(MovementStage.MOVEMENT_TO_CHOOSE_ACTION_TRANSITION,
                _chooseActionStage);
            _meleeStage.AssignExitStage(_chooseActionStage);
            _shootStage.AssignExitStage(_chooseActionStage);
        }

        public void AssignExitStage(StageBase nextStage)
        {
            _chooseActionStage.Bind(ChooseActionStage.CHOOSE_ACTION_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION,
                nextStage);
        }

        public override void Enter()
        {
            base.Enter();

            GameContext.Log("Main Unit Action stage entering child: Choose Action stage.");
            MoveToChildChooseActivation();
        }

        private void MoveToChildChooseActivation()
        {
            SignalEvent(MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION);
        }
    }
}