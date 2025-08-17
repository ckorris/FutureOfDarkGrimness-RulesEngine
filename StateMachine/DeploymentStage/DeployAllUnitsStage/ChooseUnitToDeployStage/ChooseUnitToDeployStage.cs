

namespace FDG.Stages
{
    public class ChooseUnitToDeployStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;
        public ChooseUnitToDeployStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
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
