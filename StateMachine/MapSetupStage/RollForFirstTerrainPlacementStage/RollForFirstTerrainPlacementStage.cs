namespace FDG.Stages
{
    /// <summary>
    /// Rolls off to pick which team places terrain first. In Alternating mode,
    /// records the full alternation order on the surrounding
    /// <see cref="IMapSetupContext"/>; the winning team is first, remaining teams
    /// follow in their current declaration order. Non-interactive modes ignore the
    /// recorded order but the roll still runs (and logs) for narrative
    /// consistency.
    /// </summary>
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

            if (PlaceTerrainStage.ShouldSkipTerrainPhase(context.GameContext.Settings))
            {
                context.Log("  Terrain count is 0 in Alternating mode; skipping terrain roll-off.");
                await OnRollComplete.Activate(context);
                return;
            }

            List<ITeam> teams = context.GameContext.TableState.Teams.Objects.ToList();
            List<string> teamNames = teams.Select(t => $"Team {t.TeamNumber}").ToList();

            ITeam winner = await DiceUtilities.RollOff_SingleWinner(teams, teamNames,
                context.GameContext.TextOutput, context.GameContext.Presenter, "Terrain Roll-Off");

            var order = new List<ITeam> { winner };
            foreach (var t in teams)
                if (!ReferenceEquals(t, winner))
                    order.Add(t);

            context.Log($"Team {winner.TeamNumber} won the roll-off and will place terrain first.");
            await context.Announce($"{context.GetTeamLeadName(winner)} places terrain first",
                new TextColor(120, 200, 255, 255));

            context.SetTerrainPlacementTeamOrder(order);
            await OnRollComplete.Activate(context);
        }
    }
}
