

namespace FDG.StateMachine
{
    public class ReconcileNewTurnStage : StateBase<IMainPhaseContext>
    {
        public const string TO_START_EXTRA_ACTIONS_TRANSITION = "ReconcileToStartExtraActions";

        public ReconcileNewTurnStage(StateMachine stateMachine, IMainPhaseContext context, StateBase parentState = null) : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.ReconcileNewTurnHandler.Handle(Context, MoveToStartExtraActions);
        }

        private void MoveToStartExtraActions()
        {
            SignalEvent(TO_START_EXTRA_ACTIONS_TRANSITION);
        }
    }

    public interface IReconcileNewTurnHandler : IExitOnlyHandler<IMainPhaseContext>
    {

    }
}