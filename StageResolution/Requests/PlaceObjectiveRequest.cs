using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// Asks one player to choose a position for a single objective marker.
    /// Emitted once per marker by <see cref="FDG.Stages.PlaceObjectivesStage"/>;
    /// the stage alternates which player it asks until all markers are placed.
    /// </summary>
    /// <remarks>
    /// Result is the chosen <see cref="Position"/>. The stage validates the
    /// result before committing; if invalid (e.g. stale view of the table)
    /// it re-emits the request to the same player.
    /// </remarks>
    public class PlaceObjectiveRequest : IStageTaskRequest<Position>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }

        /// <summary>1-based index of this marker among the markers to be placed this game.</summary>
        public int MarkerIndex { get; }

        /// <summary>Total markers that will be placed this game (D3+2).</summary>
        public int TotalMarkers { get; }

        /// <summary>Rectangle on the table inside which the marker must land — typically the band between deployment zones.</summary>
        public RectangularZone LegalBand { get; }

        /// <summary>Minimum center-to-center separation from previously-placed markers.</summary>
        public float MinSeparationInches { get; }

        [JsonConstructor]
        public PlaceObjectiveRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            int markerIndex, int totalMarkers, RectangularZone legalBand, float minSeparationInches)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            MarkerIndex = markerIndex;
            TotalMarkers = totalMarkers;
            LegalBand = legalBand;
            MinSeparationInches = minSeparationInches;
        }

        public PlaceObjectiveRequest(PlayerID targetPlayerID, string taskName,
            int markerIndex, int totalMarkers, RectangularZone legalBand, float minSeparationInches)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), taskName,
                   markerIndex, totalMarkers, legalBand, minSeparationInches)
        { }

        public Task<Position> Resolve(Position resolution) => Task.FromResult(resolution);
    }
}
