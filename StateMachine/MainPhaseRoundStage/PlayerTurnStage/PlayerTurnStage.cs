

namespace FDG.Stages
{
    public class PlayerTurnStage : StateBase<IMainPhaseContext>
    {
        private const string PLAYER_TURN_TO_CHILD_DETERMINE_PLAYER_TURN = "PlayerTurnToChildDeterminePlayerTurn";

        private int _enterCount = 0;

        private readonly StateMachine _stateMachine;

        public PlayerTurnStage(StateMachine stateMachine, IMainPhaseContext mainPhaseContext, 
            IPlayerTurnContext playerTurnContext, IUnitActionContext mainUnitActionContext, 
            IMeleeContext meleeContext, IRangedContext rangedContext, StateBase parentState = null)
            : base(stateMachine, mainPhaseContext, parentState)
        {
            _stateMachine = stateMachine;

            DeterminePlayerTurnStage determinePlayerTurnStage = new DeterminePlayerTurnStage(stateMachine, playerTurnContext, this);
            ChooseUnitToActivateStage chooseUnitToActivateStage = new ChooseUnitToActivateStage(stateMachine, playerTurnContext, this);
            MainUnitActionStage mainUnitActionStage = new MainUnitActionStage(stateMachine, playerTurnContext, mainUnitActionContext,
                meleeContext, rangedContext, this);
            ReconcileEndOfActivationStage reconcileEndOfActivationStage = new ReconcileEndOfActivationStage(stateMachine, playerTurnContext, this);

            stateMachine.AddTransition<DeterminePlayerTurnStage>(DeterminePlayerTurnStage.DETERMINE_PLAYER_TO_CHOOSE_UNIT_TO_ACTIVATE_TRANSITION,
                chooseUnitToActivateStage);
            stateMachine.AddTransition<ChooseUnitToActivateStage>(ChooseUnitToActivateStage.CHOOSE_UNIT_TO_ACTIVATE_TO_MAIN_UNIT_ACTION_TRANSITION,
                mainUnitActionStage);
            stateMachine.AddTransition<ReconcileEndOfActivationStage>(ReconcileEndOfActivationStage.RECONCILE_ACTIVATION_BACK_TO_DETERMINE_PLAYER_TURN_TRANSITION,
                determinePlayerTurnStage);

            stateMachine.AddTransition<PlayerTurnStage>(PLAYER_TURN_TO_CHILD_DETERMINE_PLAYER_TURN, 
                determinePlayerTurnStage);

            stateMachine.AddTransition<ChooseActionStage>(ChooseActionStage.CHOOSE_ACTION_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION,
                reconcileEndOfActivationStage);
            stateMachine.AddTransition<MovementStage>(MovementStage.MOVEMENT_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION, 
                reconcileEndOfActivationStage);
            stateMachine.AddTransition<ResolveMeleeMoraleStage>(ResolveMeleeMoraleStage.RESOLVE_MELEE_MORALE_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION,
                reconcileEndOfActivationStage);
            stateMachine.AddTransition<ResolveRangedMoraleStage>(ResolveRangedMoraleStage.RESOLVE_RANGED_MORALE_FINISHED_TRANSITION,
                reconcileEndOfActivationStage);
        }

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _stateMachine.AddTransition<ReconcileEndOfActivationStage>(ReconcileEndOfActivationStage.RECONCILE_ACTIVATION_TO_RECONCILE_OBJECTIVES_TRANSITION,
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