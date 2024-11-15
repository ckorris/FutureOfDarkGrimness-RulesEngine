
namespace FDG.Stages
{

    public class DeploymentStage : StateBase<IGameContext>
    {
        public const string TO_MAIN_TRANSITION = "DeploymentToMain";

        public DeploymentStage(StateMachine stateMachine, IGameContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.GetHandler<IDeploymentHandler>().Handle(Context, MoveToMain);
        }

        private void MoveToMain()
        {
            SignalEvent(TO_MAIN_TRANSITION);
        }
    }

    public interface IDeploymentHandler : IExitOnlyHandler<IGameContext>
    {

    }
}