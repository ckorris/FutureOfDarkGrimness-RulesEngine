
namespace FDG.Stages
{

    public class MapSetupStage : StateBase<IGameContext>
    {
        public const string TO_DEPLOYMENT_TRANSITION = "MapSetupToDeployment";

        public MapSetupStage(StateMachine stateMachine, IGameContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.GetHandler < IMapSetupHandler>().Handle(Context, MoveToDeployment);
        }

        private void MoveToDeployment()
        {
            SignalEvent(TO_DEPLOYMENT_TRANSITION);
        }
    }

    public interface IMapSetupHandler : IExitOnlyHandler<IGameContext>
    {

    }
}
