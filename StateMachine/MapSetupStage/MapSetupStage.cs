
namespace FDG.Stages
{

    public class MapSetupStage : StageBase<IGameContext>
    {
        public const string TO_DEPLOYMENT_TRANSITION = "MapSetupToDeployment";

        public StageBinding ToDeployment;

        public MapSetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent) 
            : base(gameContext, parent)
        {
            ToDeployment = new StageBinding(this);
        }

        public override void Enter(IGameContext context)
        {
            GameContext.GetHandler<IMapSetupHandler>().Handle(GameContext, ToDeployment.Activate);
        }

        public override void Exit()
        {
        }
    }

    public interface IMapSetupHandler : IExitOnlyHandler<IGameContext>
    {

    }
}
