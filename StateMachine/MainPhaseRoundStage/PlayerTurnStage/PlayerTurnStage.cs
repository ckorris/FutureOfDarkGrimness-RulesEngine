

namespace FDG.Stages
{
    public class PlayerTurnStage : StageBase<IMainPhaseContext>
    {
        private const string PLAYER_TURN_TO_CHILD_DETERMINE_PLAYER_TURN = "PlayerTurnToChildDeterminePlayerTurn";

        private int _enterCount = 0;

        private readonly ReconcileEndOfActivationStage _reconcileEndOfActivationStage;

        public PlayerTurnStage(StateMachine stateMachine, IMainPhaseContext mainPhaseContext, 
            IPlayerTurnContext playerTurnContext, IUnitActionContext mainUnitActionContext, 
            IMeleeContext meleeContext, IRangedContext rangedContext, StageBase parentState = null)
            : base(stateMachine, mainPhaseContext, parentState)
        {
            DeterminePlayerTurnStage determinePlayerTurnStage = new DeterminePlayerTurnStage(stateMachine, playerTurnContext, this);
            ChooseUnitToActivateStage chooseUnitToActivateStage = new ChooseUnitToActivateStage(stateMachine, playerTurnContext, this);
            MainUnitActionStage mainUnitActionStage = new MainUnitActionStage(stateMachine, playerTurnContext, mainUnitActionContext,
                meleeContext, rangedContext, this);
            _reconcileEndOfActivationStage = new ReconcileEndOfActivationStage(stateMachine, playerTurnContext, this);

            determinePlayerTurnStage.Bind(DeterminePlayerTurnStage.DETERMINE_PLAYER_TO_CHOOSE_UNIT_TO_ACTIVATE_TRANSITION,
                chooseUnitToActivateStage);
            chooseUnitToActivateStage.Bind(ChooseUnitToActivateStage.CHOOSE_UNIT_TO_ACTIVATE_TO_MAIN_UNIT_ACTION_TRANSITION,
                mainUnitActionStage);
            _reconcileEndOfActivationStage.Bind(ReconcileEndOfActivationStage.RECONCILE_ACTIVATION_BACK_TO_DETERMINE_PLAYER_TURN_TRANSITION,
                determinePlayerTurnStage);

            Bind(PLAYER_TURN_TO_CHILD_DETERMINE_PLAYER_TURN, determinePlayerTurnStage);

            mainUnitActionStage.AssignExitStage(_reconcileEndOfActivationStage);
        }

        public void AssignExitStage(StageBase targetStageWhenFinished)
        {
            _reconcileEndOfActivationStage.Bind(ReconcileEndOfActivationStage.RECONCILE_ACTIVATION_TO_RECONCILE_OBJECTIVES_TRANSITION,
                targetStageWhenFinished);
        }

        public override void Enter()
        {
            base.Enter();

            //Go straight to the child.
            SignalEvent(PLAYER_TURN_TO_CHILD_DETERMINE_PLAYER_TURN);
        }
    }
}