

namespace FDG.Stages
{
    public class RollForFirstDeploymentStage : StageBase<IDeploymentContext>
    {
        public StageBinding ToDeployAllUnits;

        public RollForFirstDeploymentStage(IGameContext gameContext, IStateMachineLayer<IDeploymentContext> parent)
            : base(gameContext, parent)
        {
            ToDeployAllUnits = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentContext context)
        {
            context.LogDebug("Entered Roll for First Deployment stage.");

            List<ITeam> teams = context.TableState().Teams.Objects.ToList();
            List<string> teamNames = teams.Select(team => $"Team {team.TeamNumber}").ToList();

            List<ITeam> rollOrder = await DiceUtilities.RollOff_Ordered(teams, teamNames,
                context.GameContext.TextOutput, context.GameContext.Presenter, "Deployment Roll-Off");

            ITeam winner = rollOrder.First();
            context.Log($"Team {winner.TeamNumber} won the roll-off and will deploy first.");
            await context.Announce($"{context.GetTeamLeadName(winner)} deploys first",
                new TextColor(120, 200, 255, 255));

            context.SetFirstDeploymentRollOrder(rollOrder);

            await ToDeployAllUnits.Activate(context);
        }
    }
}
