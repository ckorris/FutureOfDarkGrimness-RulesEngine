


namespace FDG.Stages
{
    public class ChooseDeployActionStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;

        public ChooseDeployActionStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.Log("Entering Choose Deploy Action stage.");

            //In the future, I expect this to be the place where you would check to see if there are options
            //to do something different, like Scout, Ambush, or place in a Transport. 
            //For now, assume we choose to deploy like normal and move to the next stage.
            OnFinish.Activate(context);
        }
    }
}
