
namespace FDG.Stages
{

    public class ReconcileObjectivesStage : StateBase<IMainPhaseContext>
    {
        public const string RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN = "ReconcileObjectivesBackToReconcileNewTurn";
        public const string RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION = "ReconcileObjectivesBackToDeterminePlayerTurn";


        private int _timesEntered = 0;

        public ReconcileObjectivesStage(StateMachine stateMachine, IMainPhaseContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            //Temp, we'll just count to three and leave the phase when we're done.
            _timesEntered++;

            Context.TextOutput.Log($"Reconcile Objectives entered for time {_timesEntered}.");

            if (_timesEntered < 4)
            {
                Context.TextOutput.Log($"Returning to reconcile new turn.");
                SignalEvent(RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN);
            }
            else
            {
                Context.TextOutput.Log($"Fourth time entered, leaving stage.");
                SignalEvent(RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION);
            }
            
        }

        
    }
}