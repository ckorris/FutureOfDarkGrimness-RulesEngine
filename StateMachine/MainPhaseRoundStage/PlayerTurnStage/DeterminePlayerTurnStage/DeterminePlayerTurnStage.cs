
namespace FDG.Stages
{

    public class DeterminePlayerTurnStage : StageBase<IPlayerTurnContext>
    {


        public StageBinding ToChooseUnitToActivate;

        public DeterminePlayerTurnStage(IGameContext gameContext, IStateMachineLayer<IPlayerTurnContext> parent) : base(gameContext, parent)
        {
            ToChooseUnitToActivate = new StageBinding(this);
        }

        public override void Enter(IPlayerTurnContext context)
        {
            GameContext.Log("Determine Player Turn Stage entered and moving through.");
            ToChooseUnitToActivate.Activate(context);
        }
    }
}