namespace FDG.Stages
{
    public class RollForFirstObjectivePlacementStage : StageBase<IGameContext>
    {
        public StageBinding OnRollComplete;

        public RollForFirstObjectivePlacementStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
            OnRollComplete = new StageBinding(this);
        }

        public override async Task Enter(IGameContext context)
        {
            context.Log($"Entered {nameof(RollForFirstObjectivePlacementStage)}.");

            List<ITeam> teams = context.TableState.Teams.Objects.ToList();
            List<string> teamNames = teams.Select(t => $"Team {t.TeamNumber}").ToList();

            ITeam winner = DiceUtilities.RollOff_SingleWinner(teams, teamNames, context.TextOutput);

            context.Log($"Team {winner.TeamNumber} won the roll-off and will place objectives first.");

            OnRollComplete.Activate(context);
        }
    }
}
