
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
            //TODO: Implement. Should set up terrain and also objectives.
            //Perhaps sub-stages? Roll for first terrain, place, roll for objective count, place.
            //Or maybe in the options you could have a pre-configured one, in which case you just do that.
            context.Log($"Entered {nameof(MapSetupStage)}.");

            ToDeployment.Activate(context);
        }
    }

}
