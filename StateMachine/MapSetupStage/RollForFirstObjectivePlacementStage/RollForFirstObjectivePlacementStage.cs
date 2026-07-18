namespace FDG.Stages
{
    /// <summary>
    /// Rolls off to pick which team places the first objective marker, then
    /// records the full alternation order on the surrounding
    /// <see cref="IMapSetupContext"/>. The winning team is first; remaining
    /// teams follow in their current declaration order. When objectives are
    /// auto-placed (<see cref="EObjectivePlacementMode.AutoPlaced"/>) no one takes
    /// turns placing them, so the roll-off - dice and the "places first"
    /// announcement - is skipped and placement proceeds in team declaration order.
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
            context.LogDebug($"Entered {nameof(RollForFirstObjectivePlacementStage)}.");

            List<ITeam> teams = context.GameContext.TableState.Teams.Objects.ToList();

            // Auto-Placed objectives don't involve players taking turns, so there's no one to roll off
            // for. Skip the dice roll-off and the "places first" beat; place in team declaration order
            // (the placement loop's cursor still needs an order even though nobody is prompted).
            if (context.GameContext.Settings.ObjectivePlacementMode == EObjectivePlacementMode.AutoPlaced)
            {
                context.Log("  Objectives are auto-placed; skipping the objective roll-off.");
                context.SetObjectivePlacementTeamOrder(teams);
                await OnRollComplete.Activate(context);
                return;
            }

            List<string> teamNames = teams.Select(t => $"Team {t.TeamNumber}").ToList();

            ITeam winner = await DiceUtilities.RollOff_SingleWinner(teams, teamNames,
                context.GameContext.TextOutput, context.GameContext.DiceRoller, context.GameContext.Presenter, "Objective Roll-Off");

            // Build the alternation order: winner first, then the rest in their existing order.
            var order = new List<ITeam> { winner };
            foreach (var t in teams)
                if (!ReferenceEquals(t, winner))
                    order.Add(t);

            context.Log($"Team {winner.TeamNumber} won the roll-off and will place objectives first.");
            await context.Announce($"{context.GetTeamLeadName(winner)} places objectives first",
                new TextColor(120, 200, 255, 255));

            context.SetObjectivePlacementTeamOrder(order);
            await OnRollComplete.Activate(context);
        }
    }
}
