

namespace FDG.Stages
{
    public class DetermineNextDeployPlayerStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;
        public StageBinding OnFinishedDeployingAllUnits;

        public DetermineNextDeployPlayerStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
            OnFinishedDeployingAllUnits = new StageBinding(this);
        }

        public override Task Enter(IDeploymentTurnContext context)
        {
            throw new NotImplementedException();
        }
    }
}
