
namespace FDG.StateMachine
{

    public class ResolveMeleeMoraleStage : StateBase<IMeleeContext>
    {
        public const string RESOLVE_MELEE_MORALE_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION =
            "ResolveMeleeMoraleToReconcileEndOfActivation";

        public ResolveMeleeMoraleStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Resolving melee morale.");
            MoveToReconcileEndOfActivation();
        }

        private void MoveToReconcileEndOfActivation()
        {
            SignalEvent(RESOLVE_MELEE_MORALE_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION);
        }
    }
}