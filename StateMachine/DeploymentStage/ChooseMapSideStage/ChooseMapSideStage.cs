

namespace FDG.Stages
{

    public class ChooseMapSideStage : StageBase<IDeploymentContext>
    {
        public StageBinding ToRollForFirstDeployment;

        public ChooseMapSideStage(IGameContext gameContext, IStateMachineLayer<IDeploymentContext> parent)
            : base(gameContext, parent)
        {
            ToRollForFirstDeployment = new StageBinding(this);
        }

        public override Task Enter(IDeploymentContext context)
        {
            throw new NotImplementedException();
        }
    }
}
