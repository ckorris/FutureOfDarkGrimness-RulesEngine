
namespace FDG.Stages
{

    public class DeploymentStage : StageBase<IGameContext>
    {
        //TODO: Will likely have to break into sub-stages in order to handle initial role, scout, ambush, and other things.

        public const string TO_MAIN_TRANSITION = "DeploymentToMain";

        public StageBinding ToMain;

        public DeploymentStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent) : base(gameContext, parent)
        {
            ToMain = new StageBinding(this);
        }

        public override void Enter(IGameContext context)
        {
            context.Log($"Entered {nameof(DeploymentStage)}.");
            //GameContext.GetHandler<IDeploymentHandler>().Handle(GameContext, ToMain.Activate);
            //TODO: Make list of all choices, put in some kind of context object, then repeat handler call
            //until all things have been positioned.
            //Also needs a way to validate valid placement.
            
        }
    }

    public interface IDeploymentHandler
    {
        
    }
}