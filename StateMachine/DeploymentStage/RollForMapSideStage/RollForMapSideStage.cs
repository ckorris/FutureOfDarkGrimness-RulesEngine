
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
            Random random = new Random();

            List<ITeam> teams = context.TableState().Teams.Objects.ToList();
            List<string> teamNames = teams.Select(team => $"Team {team.TeamNumber}").ToList();

            //TODO: Show visuals of dice, but we also need to add that below.
            List<ITeam> rollOrder = DiceUtilities.RollOff_Ordered(teams, teamNames, context.GameContext.TextOutput);

            context.Log($"Team {rollOrder.First().TeamNumber} won the roll-off and will choose their side of the map.");

            context.SetMapSideRollWinner(rollOrder);

            await OnFinish.Activate(context);
        }
    }
}
