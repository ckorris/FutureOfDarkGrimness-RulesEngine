
namespace FDG.Stages
{

    public class DeploymentStage : StageBase<IGameContext>
    {
        public const string TO_MAIN_TRANSITION = "DeploymentToMain";

        public StageBinding ToMain;

        public DeploymentStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent) : base(gameContext, parent)
        {
            ToMain = new StageBinding(this);
        }

        public override void Enter(IGameContext context)
        {
            context.Log($"Entered {nameof(DeploymentStage)}.");
            GameContext.GetHandler<IDeploymentHandler>().Handle(GameContext, ToMain.Activate);
        }
    }

    public interface IDeploymentHandler : IExitOnlyHandler<IGameContext>
    {

    }
}