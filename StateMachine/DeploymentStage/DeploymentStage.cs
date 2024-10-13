
namespace FDG.StateMachine
{

    public class DeploymentStage : StateBase<ITopLevelContext>
    {
        public const string TO_MAIN_TRANSITION = "DeploymentToMain";

        public DeploymentStage(StateMachine stateMachine, ITopLevelContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.DeploymentHandler.Handle(Context, MoveToMain);
        }

        private void MoveToMain()
        {
            SignalEvent(TO_MAIN_TRANSITION);
        }
    }

    public interface IDeploymentHandler : IExitOnlyHandler<ITopLevelContext>
    {

    }
}