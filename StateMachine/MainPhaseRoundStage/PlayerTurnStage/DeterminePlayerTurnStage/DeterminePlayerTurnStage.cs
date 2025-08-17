
namespace FDG.Stages
{

    public class DeterminePlayerTurnStage : StageBase<IPlayerTurnContext>
    {


        public StageBinding ToChooseUnitToActivate;

        public DeterminePlayerTurnStage(IGameContext gameContext, IStateMachineLayer<IPlayerTurnContext> parent) : base(gameContext, parent)
        {
            ToChooseUnitToActivate = new StageBinding(this);
        }

        public override async Task Enter(IPlayerTurnContext context)
        {
            //NOTE: See DetermineNextDeployPlayerStage for code that iterates through teams and players to see who should deploy next.
            //Might be able to move that code to a utility somehow, though differences exist.

            GameContext.Log("Determine Player Turn Stage entered and moving through.");
            ToChooseUnitToActivate.Activate(context);
        }
    }
}