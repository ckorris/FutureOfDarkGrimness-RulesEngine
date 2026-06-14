namespace FDG.Stages
{
    public class DetermineNextObjectivePlacerStage : StageBase<IObjectivePlacementTurnContext>
    {
        public StageBinding OnPlacerDetermined;
        public StageBinding OnAllMarkersPlaced;

        public DetermineNextObjectivePlacerStage(IGameContext gameContext,
            IStateMachineLayer<IObjectivePlacementTurnContext> parent)
            : base(gameContext, parent)
        {
            OnPlacerDetermined = new StageBinding(this);
            OnAllMarkersPlaced = new StageBinding(this);
        }

        public override async Task Enter(IObjectivePlacementTurnContext context)
        {
            context.Log($"Entered {nameof(DetermineNextObjectivePlacerStage)}.");

            if (!context.HasRemainingMarkers())
            {
                context.Log($"All {context.TotalMarkers} objective markers placed.");
                await OnAllMarkersPlaced.Activate(context);
                return;
            }

            // First placement uses the cursor's initial state (the roll-off winner's team, that team's first player).
            if (!context.HasStarted)
            {
                context.HasStarted = true;
                var team = context.Cursor.GetCurrentTeam();
                var player = context.Cursor.GetCurrentPlayerID();
                context.Log($"Team {team.TeamNumber} (player {player.ID}) will place objective {context.MarkersPlaced + 1} of {context.TotalMarkers}.");
                await OnPlacerDetermined.Activate(context);
                return;
            }

            // Otherwise round-robin. Every team always has "remaining work" until total count is reached —
            // the bookkeeping is on the per-marker counter, not on per-team quotas.
            bool advanced = context.Cursor.TryAdvance(
                teamHasRemainingWork: _ => context.HasRemainingMarkers(),
                playerHasRemainingWork: _ => context.HasRemainingMarkers(),
                out ITeam? nextTeam,
                out PlayerID? nextPlayerID);

            if (!advanced || nextTeam == null || nextPlayerID == null)
            {
                // Defensive — HasRemainingMarkers already returned true so this shouldn't happen.
                throw new InvalidOperationException("Cursor failed to advance despite markers remaining.");
            }

            context.Log($"Team {nextTeam.TeamNumber} (player {nextPlayerID.Value.ID}) will place objective {context.MarkersPlaced + 1} of {context.TotalMarkers}.");
            await OnPlacerDetermined.Activate(context);
        }
    }
}
