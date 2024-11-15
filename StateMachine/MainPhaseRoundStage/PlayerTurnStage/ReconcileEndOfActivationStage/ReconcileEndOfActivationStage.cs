
namespace FDG.Stages
{

    public class ReconcileEndOfActivationStage : StateBase<IPlayerTurnContext>
    {
        public const string RECONCILE_ACTIVATION_BACK_TO_DETERMINE_PLAYER_TURN_TRANSITION
            = "ReconcileEndOfActivationBackToDeterminePlayerTurn";
        public const string RECONCILE_ACTIVATION_TO_RECONCILE_OBJECTIVES_TRANSITION
            = "PlayerTurnToReconcileObjectives";

        public ReconcileEndOfActivationStage(StateMachine stateMachine, IPlayerTurnContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        int _enterCount = 0;

        public override void Enter()
        {
            base.Enter();

            //Temp, just have it count to 3, as if there are three units to activate.
            _enterCount++;

            if (_enterCount < 3)
            {
                Context.Log($"ReconcileEndOfActivationStage entrance {_enterCount}. Restarting turn.");
                SignalEvent(RECONCILE_ACTIVATION_BACK_TO_DETERMINE_PLAYER_TURN_TRANSITION);
            }
            else
            {
                Context.Log("ReconcileEndOfActivationStage entrance 3. Ending round, moving to reconcile objectives.");
                _enterCount = 0;
                SignalEvent(RECONCILE_ACTIVATION_TO_RECONCILE_OBJECTIVES_TRANSITION);
            }
        }

    }
}