namespace FDG.Stages
{
    public class RollForFirstTerrainPlacementStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnRollComplete;

        public RollForFirstTerrainPlacementStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnRollComplete = new StageBinding(this);
        }

        public override async Task Enter(IMapSetupContext context)
        {
            context.Log($"Entered {nameof(RollForFirstTerrainPlacementStage)}.");

            List<ITeam> teams = context.GameContext.TableState.Teams.Objects.ToList();
            List<string> teamNames = teams.Select(t => $"Team {t.TeamNumber}").ToList();

            ITeam winner = DiceUtilities.RollOff_SingleWinner(teams, teamNames, context.GameContext.TextOutput);

            context.Log($"Team {winner.TeamNumber} won the roll-off and will place terrain first.");

            OnRollComplete.Activate(context);
        }
    }
}
