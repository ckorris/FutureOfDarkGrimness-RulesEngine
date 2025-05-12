
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

        public override async Task Enter(IGameContext context)
        {
            //TODO: Implement.
            context.Log($"Entered {nameof(MapSetupStage)}.");

            ToDeployment.Activate(context);
        }
    }

}
