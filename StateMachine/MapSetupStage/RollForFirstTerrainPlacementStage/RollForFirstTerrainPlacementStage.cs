namespace FDG.Stages
{
    /// <summary>
    /// Rolls off to pick which team places terrain first, and records the full alternation order on the
    /// surrounding <see cref="IMapSetupContext"/> (winning team first, the rest in declaration order) for
    /// <see cref="PlaceTerrainStage"/>'s Alternating mode. When terrain is placed automatically
    /// (AutoFromLayout / LoadFromFile) or there are no alternating pieces, no one takes turns placing terrain,
    /// so the roll-off is skipped entirely — setup goes straight to the objective roll-off and placement.
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

            // The roll-off only sets the alternation order for player-placed (Alternating) terrain. When terrain
            // is placed automatically — or there are no alternating pieces — no one places terrain by taking
            // turns, so skip the roll-off and go straight to terrain auto-placement + the objective phase.
            if (!PlaceTerrainStage.NeedsTerrainRollOff(context.GameContext.Settings))
            {
                context.Log("  Terrain is placed automatically (or has no alternating pieces); skipping the terrain roll-off.");
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
