
namespace FDG.StateMachine
{

    public class MainPhaseRoundStage : StateBase<ITopLevelContext>
    {
        public const string MAIN_TO_RECONCILE_VICTORY_CALCULATION = "MainToReconcileVictoryCalculation";
        
        private  const string MAIN_TO_RECONCILE_NEW_TURN_STAGE = "MainToReconcileNewTurn";

        private IMainPhaseContext _mainPhaseContext;

        private readonly StateMachine _stateMachine;

        public MainPhaseRoundStage(StateMachine stateMachine, ITopLevelContext context,
            IMainPhaseContext mainPhaseContext, IPlayerTurnContext playerTurnContext,
            IUnitActionContext unitActionContext, IMeleeContext meleeContext, IRangedContext rangedContext)
            : base(stateMachine, context, null)
        {
            _stateMachine = stateMachine;

            ReconcileNewTurnStage reconcileNewTurnStage = new ReconcileNewTurnStage(stateMachine, mainPhaseContext, this);
            StartOfTurnExtraActionStage startOfTurnExtraActionStage = new StartOfTurnExtraActionStage(stateMachine, mainPhaseContext, this);
            DetermineFirstPlayerTurnStage determineFirstPlayerTurnStage = new DetermineFirstPlayerTurnStage(stateMachine, mainPhaseContext, this);
            PlayerTurnStage playerTurnStage = new PlayerTurnStage(stateMachine, mainPhaseContext, playerTurnContext,
                unitActionContext, meleeContext, rangedContext, this);
            ReconcileObjectivesStage reconcileObjectivesStage = new ReconcileObjectivesStage(stateMachine, mainPhaseContext, this);

            playerTurnStage.AssignExitStage(reconcileObjectivesStage);

            //Entrance.
            stateMachine.AddTransition<MainPhaseRoundStage>(MAIN_TO_RECONCILE_NEW_TURN_STAGE, reconcileNewTurnStage);

            //Main stage internal transitions.
            stateMachine.AddTransition<ReconcileNewTurnStage>(ReconcileNewTurnStage.TO_START_EXTRA_ACTIONS_TRANSITION, 
                startOfTurnExtraActionStage);
            stateMachine.AddTransition<StartOfTurnExtraActionStage>(StartOfTurnExtraActionStage.TO_DETERMINE_FIRST_TURN_TRANSITION, 
                determineFirstPlayerTurnStage);
            stateMachine.AddTransition<DetermineFirstPlayerTurnStage>(DetermineFirstPlayerTurnStage.DETERMINE_FIRST_PLAYER_TO_PLAYER_TURN_TRANSITION, 
                playerTurnStage);
            stateMachine.AddTransition<ReconcileObjectivesStage>(ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN,
                reconcileNewTurnStage);

            //Setting up child binding. Want to move to child sometime. 
            //stateMachine.AddTransition<ReconcileEndOfActivationStage>(ReconcileEndOfActivationStage.RECONCILE_ACTIVATION_TO_RECONCILE_OBJECTIVES_TRANSITION,
            //    reconcileObjectivesStage);
        }

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _stateMachine.AddTransition<ReconcileObjectivesStage>(ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION,
                targetStageWhenFinished);
        }

        public override void Enter()
        {
            base.Enter();

            Context.TextOutput.Log($"Main Phase stage entering child: Reconcile New Turn.");
            MoveToReconcileNewTurn();
        }

        private void MoveToReconcileVictoryCalculation()
        {
            SignalEvent(MAIN_TO_RECONCILE_VICTORY_CALCULATION);
        }
        private void MoveToReconcileNewTurn()
        {
            SignalEvent(MAIN_TO_RECONCILE_NEW_TURN_STAGE);
        }
    }
}