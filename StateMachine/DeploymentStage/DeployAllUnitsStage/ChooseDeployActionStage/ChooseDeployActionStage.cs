


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

        public override Task Enter(IDeploymentTurnContext context)
        {
            throw new NotImplementedException();
        }
    }
}
