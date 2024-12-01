
using System.Collections.Generic;

namespace FDG.Stages
{

    public class MainPhaseRoundStage : ParentStage<IGameContext, IMainPhaseContext>
    {
        public const string MAIN_TO_RECONCILE_VICTORY_CALCULATION = "MainToReconcileVictoryCalculation";
        
        private const string MAIN_TO_RECONCILE_NEW_TURN_STAGE = "MainToReconcileNewTurn";

        public StageBinding ToReconcileNewTurn;
        public StageBinding ToVictoryCalculation;

        public MainPhaseRoundStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent) : base(gameContext, parent)
        {
            ToReconcileNewTurn = new StageBinding(this);
            ToVictoryCalculation = new StageBinding(this);
        }

        /*
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
        */

        public override void Enter(IGameContext context)
        {
            GameContext.TextOutput.Log($"Main Phase stage entering child: Reconcile New Turn.");

            base.Enter(context);
        }

        protected override IMainPhaseContext GetNewChildContext(IGameContext contextSelf)
        {
            return new MainPhaseContext(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMainPhaseContext> startingChild)
        {
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ReconcileNewTurnStage(GameContext, this), out var reconcileNewTurn)
                .AddChild(new StartOfTurnExtraActionStage(GameContext, this), out var startOfTurnExtraActions)
                .AddChild(new DetermineFirstPlayerTurnStage(GameContext, this), out var determineFirstPlayerTurn)
                .AddChild(new PlayerTurnStage(GameContext, this), out var playerTurn)
                .AddChild(new ReconcileObjectivesStage(GameContext, this), out var reconcileObjectives)
                .AddSibling(nameof(ToReconcileNewTurn), ToReconcileNewTurn, out string reconcileNewTurnEvent)
                .AddSibling(nameof(ToVictoryCalculation), ToVictoryCalculation, out string toVictoryCalculationEvent)
                .Build();

            startingChild = reconcileNewTurn;

            reconcileNewTurn.ToStartExtraActions.Bind(startOfTurnExtraActions);
            startOfTurnExtraActions.ToDetermineFirstTurn.Bind(determineFirstPlayerTurn);
            determineFirstPlayerTurn.ToPlayerTurn.Bind(playerTurn);
            playerTurn.OnTurnFinished.Bind(reconcileObjectives);
            reconcileObjectives.ToReconcileEndOfTurn.Bind(reconcileNewTurn);
            reconcileObjectives.ToVictoryCalculation.Bind(toVictoryCalculationEvent);

            return dictionary;
        }
    }
}