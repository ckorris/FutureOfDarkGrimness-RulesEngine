
namespace FDG.Stages
{

    public class MainPhaseRoundStage : ParentStage<IGameContext, IMainPhaseContext>
    {
        public StageBinding? ToReconcileNewTurn;
        public StageBinding? ToVictoryCalculation;

        public MainPhaseRoundStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        public override async Task Enter(IGameContext context)
        {
            GameContext.TextOutput.Log($"Main Phase stage entering child: Reconcile New Turn.");

            base.Enter(context);
        }

        protected override IMainPhaseContext GetNewChildContext(IGameContext contextSelf)
        {
            if(contextSelf.FirstDeploymentRollOrder == null)
            {
                throw new NullReferenceException($"{nameof(contextSelf.FirstDeploymentRollOrder)} in parent context was null when getting " + 
                    $"child context in {nameof(MainPhaseRoundStage)}.");
            }

            return new MainPhaseContext(GameContext, contextSelf.FirstDeploymentRollOrder);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMainPhaseContext> startingChild)
        {
            ToReconcileNewTurn = new StageBinding(this);
            ToVictoryCalculation = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ReconcileNewRoundStage(GameContext, this), out var reconcileNewTurn)
                .AddChild(new StartOfRoundExtraActionStage(GameContext, this), out var startOfTurnExtraActions)
                .AddChild(new PlayerTurnStage(GameContext, this), out var playerTurn)
                .AddChild(new ReconcileObjectivesStage(GameContext, this), out var reconcileObjectives)
                .AddSibling(nameof(ToReconcileNewTurn), ToReconcileNewTurn, out string reconcileNewTurnEvent)
                .AddSibling(nameof(ToVictoryCalculation), ToVictoryCalculation, out string toVictoryCalculationEvent)
                .Build();

            startingChild = reconcileNewTurn;

            reconcileNewTurn.ToStartExtraActions.Bind(startOfTurnExtraActions);
            startOfTurnExtraActions.OnFinished.Bind(playerTurn);
            playerTurn.OnTurnFinished.Bind(reconcileObjectives);
            reconcileObjectives.ToReconcileEndOfTurn.Bind(reconcileNewTurn);
            reconcileObjectives.ToVictoryCalculation.Bind(toVictoryCalculationEvent);

            return dictionary;
        }
    }
}