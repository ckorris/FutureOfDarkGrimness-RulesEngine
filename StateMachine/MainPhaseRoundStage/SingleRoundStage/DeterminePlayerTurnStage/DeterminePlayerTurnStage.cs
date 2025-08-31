
namespace FDG.Stages
{

    public class DeterminePlayerTurnStage : StageBase<ISingleRoundContext>
    {
        public StageBinding OnDeterminedPlayerTurn;
        public StageBinding OnNoPlayersLeft;

        public DeterminePlayerTurnStage(IGameContext gameContext, IStateMachineLayer<ISingleRoundContext> parent) : base(gameContext, parent)
        {
            OnDeterminedPlayerTurn = new StageBinding(this);
            OnNoPlayersLeft = new StageBinding(this);
        }

        public override async Task Enter(ISingleRoundContext context)
        {
            //NOTE: See DetermineNextDeployPlayerStage for code that iterates through teams and players to see who should deploy next.
            //Might be able to move that code to a utility somehow, though differences exist.

            context.Log("Entering Determine Next Player Turn stage.");

            if(context.TryAdvanceToNextPlayer(out ITeam? nextTeam, out PlayerID? nextPlayerID) == false)
            {
                OnNoPlayersLeft.Activate(context);
                return;
            }

            OnDeterminedPlayerTurn.Activate(context);
        }
    }
}