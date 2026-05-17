using FDG.Utilities;

namespace FDG.Stages
{
    public interface IObjectivePlacementTurnContext : IGameContextAccessor
    {
        bool HasStarted { get; set; }

        int MarkersPlaced { get; set; }

        int TotalMarkers { get; }

        RectangularZone LegalBand { get; }

        TeamPlayerAlternationCursor Cursor { get; }

        PlayerID GetCurrentPlacingPlayerID();

        /// <summary>True until every marker has been placed.</summary>
        bool HasRemainingMarkers();
    }

    public class ObjectivePlacementTurnContext : IObjectivePlacementTurnContext
    {
        public IGameContext GameContext { get; }

        public bool HasStarted { get; set; } = false;

        public int MarkersPlaced { get; set; } = 0;

        public int TotalMarkers { get; }

        public RectangularZone LegalBand { get; }

        public TeamPlayerAlternationCursor Cursor { get; }

        public ObjectivePlacementTurnContext(IGameContext gameContext, IReadOnlyList<ITeam> teamOrder,
            int totalMarkers, RectangularZone legalBand)
        {
            GameContext = gameContext;
            TotalMarkers = totalMarkers;
            LegalBand = legalBand;
            Cursor = new TeamPlayerAlternationCursor(teamOrder);
        }

        public PlayerID GetCurrentPlacingPlayerID() => Cursor.GetCurrentPlayerID();

        public bool HasRemainingMarkers() => MarkersPlaced < TotalMarkers;
    }
}
