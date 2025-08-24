

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
            context.Log("Entered Roll for First Deployment stage.");

            Random random = new Random();

            List<ITeam> teams = context.TableState().Teams.Objects.ToList();
            List<string> teamNames = teams.Select(team => $"Team {team.TeamNumber}").ToList();

            //TODO: Show visuals of dice, but we also need to add that below.
            List<ITeam> rollOrder = DiceUtilities.RollOff_Ordered(teams, teamNames, context.GameContext.TextOutput);

            context.Log($"Team {rollOrder.First().TeamNumber} won the roll-off and will deploy first.");

            context.SetFirstDeploymentRollOrder(rollOrder);

            ToDeployAllUnits.Activate(context);
        }
    }
}
