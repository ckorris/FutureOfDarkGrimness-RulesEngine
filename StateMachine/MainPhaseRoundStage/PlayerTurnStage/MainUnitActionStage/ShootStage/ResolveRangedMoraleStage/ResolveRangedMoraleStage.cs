
namespace FDG.Stages
{

    public class ResolveRangedMoraleStage : StateBase<IRangedContext>
    {
        public const string RESOLVE_RANGED_MORALE_FINISHED_TRANSITION =
            "ResolveRangedMoraleFinished";

        public ResolveRangedMoraleStage(StateMachine stateMachine, IRangedContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Resolving ranged morale.");
            MoveToReconcileEndOfActivation();
        }

        private void MoveToReconcileEndOfActivation()
        {
            SignalEvent(RESOLVE_RANGED_MORALE_FINISHED_TRANSITION);
        }
    }
}