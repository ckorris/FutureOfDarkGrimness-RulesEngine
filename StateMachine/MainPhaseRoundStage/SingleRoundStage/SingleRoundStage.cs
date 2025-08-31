

namespace FDG.Stages
{
    public class SingleRoundStage : ParentStage<IMainPhaseContext, ISingleRoundContext>
    {
        public StageBinding OnRoundFinished;

        public SingleRoundStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent)
            : base(gameContext, parent)
        {
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ISingleRoundContext> startingChild)
        {
            OnRoundFinished = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DeterminePlayerTurnStage(GameContext, this), out var determinePlayerTurnStage)
                .AddChild(new SingleTurnStage(GameContext, this), out var singleTurnStage)
                .AddSibling(nameof(OnRoundFinished), OnRoundFinished, out string roundFinishedEventName)
                .Build();

            startingChild = determinePlayerTurnStage;
            determinePlayerTurnStage.OnDeterminedPlayerTurn.Bind(singleTurnStage);
            determinePlayerTurnStage.OnNoPlayersLeft.Bind(roundFinishedEventName);
            singleTurnStage.OnTurnFinished.Bind(determinePlayerTurnStage);

            return dictionary;
        }

        protected override ISingleRoundContext GetNewChildContext(IMainPhaseContext contextSelf)
        {
            return new SingleRoundContext(GameContext, contextSelf.TeamActivateOrder);
        }

        protected override void ReconcileChildContextBeforeLeaving(IMainPhaseContext selfContext, ISingleRoundContext childContext)
        {
            base.ReconcileChildContextBeforeLeaving(selfContext, childContext);

            selfContext.OnEndOfRound(childContext.CurrentRoundTeamFinishOrder);
        }
    }
}