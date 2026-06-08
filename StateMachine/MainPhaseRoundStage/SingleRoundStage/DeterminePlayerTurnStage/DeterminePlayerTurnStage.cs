
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

            // Rolling save point (#052): snapshot the flow state at the start of each activation
            // cycle, before the next unit is chosen and before it is marked activated. A load taken
            // at any moment resumes from the most recent snapshot, re-playing the activation that was
            // in progress. Guarded so minimal-store unit tests (no GameProgressData type) are unaffected.
            if (GameContext.GameDataStore.IsTypeAssigned<GameProgressData>())
            {
                GameProgressUtilities.WriteProgress(
                    GameContext.GameDataStore,
                    GameProgressUtilities.Capture(context, GameContext.Settings, EResumeStage.MainPhase));
            }

            if(context.TryAdvanceToNextPlayer(out ITeam? nextTeam, out PlayerID? nextPlayerID) == false)
            {
                context.Log("No players left to activate. Ending round.");
                OnNoPlayersLeft.Activate(context);
                return;
            }

            OnDeterminedPlayerTurn.Activate(context);
        }
    }
}