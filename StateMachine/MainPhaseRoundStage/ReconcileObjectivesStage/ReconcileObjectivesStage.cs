
namespace FDG.Stages
{

    public class ReconcileObjectivesStage : StageBase<IMainPhaseContext>
    {
        public const string RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN = "ReconcileObjectivesBackToReconcileNewTurn";
        public const string RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION = "ReconcileObjectivesBackToDeterminePlayerTurn";

        public StageBinding ToReconcileEndOfTurn;
        public StageBinding ToVictoryCalculation;

        private int _timesEntered = 0;

        public ReconcileObjectivesStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            ToReconcileEndOfTurn = new StageBinding(this);
            ToVictoryCalculation = new StageBinding(this);
        }

        public override void Enter(IMainPhaseContext context)
        {
            //Temp, we'll just count to three and leave the phase when we're done.
            _timesEntered++;

            GameContext.Log($"Reconcile Objectives entered for time {_timesEntered}.");

            if (_timesEntered < 4)
            {
                GameContext.Log($"Returning to reconcile new turn.");
                ToReconcileEndOfTurn.Activate(context);
            }
            else
            {
                GameContext.Log($"Fourth time entered, leaving stage.");
                ToVictoryCalculation.Activate(context);
            }
        }
    }
}