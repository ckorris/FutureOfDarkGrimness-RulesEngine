
namespace FDG.Stages
{

    public class MainPhaseRoundStage : StateBase<IGameContext>
    {
        public const string MAIN_TO_RECONCILE_VICTORY_CALCULATION = "MainToReconcileVictoryCalculation";
        
        private  const string MAIN_TO_RECONCILE_NEW_TURN_STAGE = "MainToReconcileNewTurn";

        private IMainPhaseContext _mainPhaseContext;

        private readonly StateMachine _stateMachine;

        private ReconcileObjectivesStage _reconcileObjectivesStage;

        public MainPhaseRoundStage(StateMachine stateMachine, IGameContext context,
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
            _reconcileObjectivesStage = new ReconcileObjectivesStage(stateMachine, mainPhaseContext, this);

            playerTurnStage.AssignExitStage(_reconcileObjectivesStage);

            //Entrance.
            Bind(MAIN_TO_RECONCILE_NEW_TURN_STAGE, reconcileNewTurnStage);

            //Main stage internal transitions.
            reconcileNewTurnStage.Bind(ReconcileNewTurnStage.TO_START_EXTRA_ACTIONS_TRANSITION, 
                startOfTurnExtraActionStage);
            startOfTurnExtraActionStage.Bind(StartOfTurnExtraActionStage.TO_DETERMINE_FIRST_TURN_TRANSITION, 
                determineFirstPlayerTurnStage);
            determineFirstPlayerTurnStage.Bind(DetermineFirstPlayerTurnStage.DETERMINE_FIRST_PLAYER_TO_PLAYER_TURN_TRANSITION, 
                playerTurnStage);
            _reconcileObjectivesStage.Bind(ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN,
                reconcileNewTurnStage);

            //Setting up child binding. Want to move to child sometime. 
            //stateMachine.AddTransition<ReconcileEndOfActivationStage>(ReconcileEndOfActivationStage.RECONCILE_ACTIVATION_TO_RECONCILE_OBJECTIVES_TRANSITION,
            //    reconcileObjectivesStage);
        }

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _reconcileObjectivesStage.Bind(ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION,
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