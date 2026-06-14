namespace FDG.Stages
{
    /// <summary>
    /// Rolls off to pick which team places the first objective marker, then
    /// records the full alternation order on the surrounding
    /// <see cref="IMapSetupContext"/>. The winning team is first; remaining
    /// teams follow in their current declaration order.
    /// </summary>
    public class RollForFirstObjectivePlacementStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnRollComplete;

        public RollForFirstObjectivePlacementStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnRollComplete = new StageBinding(this);
        }

        public override async Task Enter(IMapSetupContext context)
        {
            context.Log($"Entered {nameof(RollForFirstObjectivePlacementStage)}.");

            List<ITeam> teams = context.GameContext.TableState.Teams.Objects.ToList();
            List<string> teamNames = teams.Select(t => $"Team {t.TeamNumber}").ToList();

            ITeam winner = DiceUtilities.RollOff_SingleWinner(teams, teamNames, context.GameContext.TextOutput);

            // Build the alternation order: winner first, then the rest in their existing order.
            var order = new List<ITeam> { winner };
            foreach (var t in teams)
                if (!ReferenceEquals(t, winner))
                    order.Add(t);

            context.Log($"Team {winner.TeamNumber} won the roll-off and will place objectives first.");

            context.SetObjectivePlacementTeamOrder(order);
            await OnRollComplete.Activate(context);
        }
    }
}
