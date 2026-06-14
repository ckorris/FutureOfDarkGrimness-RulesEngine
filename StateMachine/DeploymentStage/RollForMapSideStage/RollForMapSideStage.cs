
using System.Text;

namespace FDG.Stages
{
    public class RollForMapSideStage : StageBase<IDeploymentContext>
    {
        public StageBinding OnFinish;

        public RollForMapSideStage(IGameContext gameContext, IStateMachineLayer<IDeploymentContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentContext context)
        {
            List<ITeam> teams = context.TableState().Teams.Objects.ToList();
            List<string> teamNames = teams.Select(team => $"Team {team.TeamNumber}").ToList();

            List<ITeam> rollOrder = await DiceUtilities.RollOff_Ordered(teams, teamNames,
                context.GameContext.TextOutput, context.GameContext.Presenter, "Map Side Roll-Off");

            ITeam winner = rollOrder.First();
            context.Log($"Team {winner.TeamNumber} won the roll-off and will choose their side of the map.");
            await context.Announce($"{context.GetTeamLeadName(winner)} chooses their map side first",
                new TextColor(120, 200, 255, 255));

            context.SetMapSideRollWinner(rollOrder);

            OnFinish.Activate(context);
        }
    }
}
