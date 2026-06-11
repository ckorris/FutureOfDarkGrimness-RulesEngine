
namespace FDG.Stages
{

    public class MainPhaseRoundStage : ParentStage<IGameContext, IMainPhaseContext>
    {
        public StageBinding? ToReconcileNewRound;
        public StageBinding? ToVictoryCalculation;

        // Captured in PopulateTransitions so a resumed game can jump straight to it, skipping the
        // round-setup children (which already ran before the save).
        private SingleRoundStage? _singleRoundStage;

        public MainPhaseRoundStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        protected override (StageBase<IMainPhaseContext> Child, IMainPhaseContext Context)? GetResumeEntry(IGameContext contextSelf)
        {
            if (contextSelf.ResumeProgress == null)
            {
                return null;
            }

            // Resume the in-progress round directly at SingleRoundStage. ReconcileNewRound and
            // StartOfRoundExtraAction (reserve arrival) already ran this round before the save; the
            // normal round loop runs them again at the start of every subsequent round.
            IMainPhaseContext restored = GameProgressUtilities.RestoreMainPhaseContext(contextSelf, contextSelf.ResumeProgress);
            return (_singleRoundStage!, restored);
        }

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
            ToReconcileNewRound = new StageBinding(this);
            ToVictoryCalculation = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ReconcileNewRoundStage(GameContext, this), out var reconcileNewTurn)
                .AddChild(new StartOfRoundExtraActionStage(GameContext, this), out var startOfTurnExtraActions)
                .AddChild(new SingleRoundStage(GameContext, this), out var playerTurn)
                .AddChild(new ReconcileObjectivesStage(GameContext, this), out var reconcileObjectives)
                .AddSibling(nameof(ToReconcileNewRound), ToReconcileNewRound, out string reconcileNewTurnEvent)
                .AddSibling(nameof(ToVictoryCalculation), ToVictoryCalculation, out string toVictoryCalculationEvent)
                .Build();

            startingChild = reconcileNewTurn;
            _singleRoundStage = playerTurn;

            reconcileNewTurn.ToStartExtraActions.Bind(startOfTurnExtraActions);
            startOfTurnExtraActions.OnFinished.Bind(playerTurn);
            playerTurn.OnRoundFinished.Bind(reconcileObjectives);
            reconcileObjectives.ToReconcileEndOfTurn.Bind(reconcileNewTurn);
            reconcileObjectives.ToVictoryCalculation.Bind(toVictoryCalculationEvent);

            return dictionary;
        }
    }
}