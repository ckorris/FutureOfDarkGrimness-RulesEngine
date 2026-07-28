using FDG.SaveLoad;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// Result of a single terrain placement: which template in the pool the player
    /// picked, and the world-space center where its footprint should land. The
    /// stage translates the template's shape so its center lands at
    /// <see cref="Center"/> before creating the <c>TerrainData</c>.
    /// </summary>
    public class TerrainPlacementResult
    {
        public int TemplateIndex { get; }

        public Float2 Center { get; }

        /// <summary>Rotation applied to the template before translation, in degrees. 0 = no rotation.</summary>
        public float RotationDegrees { get; }

        [JsonConstructor]
        public TerrainPlacementResult(int templateIndex, Float2 center, float rotationDegrees)
        {
            TemplateIndex = templateIndex;
            Center = center;
            RotationDegrees = rotationDegrees;
        }

        public TerrainPlacementResult(int templateIndex, Float2 center)
            : this(templateIndex, center, 0f) { }
    }

    /// <summary>
    /// Asks one player to pick a template from the pool and place it on the table.
    /// Emitted once per piece in Alternating mode until
    /// <see cref="GameSettings.TerrainPieceCount"/> pieces have been placed.
    /// </summary>
    /// <remarks>
    /// Existing terrain isn't snapshotted on the request — resolvers should read
    /// it live from <c>ITableState.Terrain</c> so they always see the current
    /// state when the validator runs.
    /// </remarks>
    public class PlaceOneTerrainRequest : IStageTaskRequest<TerrainPlacementResult>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }

        /// <summary>Pieces already placed before this request (0-based for the next one).</summary>
        public int PiecesPlaced { get; }

        /// <summary>Total pieces to place this game. 0 in points mode, where no piece count is fixed up front.</summary>
        public int TotalPieces { get; }

        /// <summary>Template pool the player chooses from. The same pool is sent every turn — duplicates are allowed and templates do not deplete.</summary>
        public IReadOnlyList<TerrainPieceEntry> Pool { get; }

        public float TableWidthInches { get; }

        public float TableHeightInches { get; }

        /// <summary>
        /// #301 - the requesting player's point state in "Alternating: Points" mode; null in every
        /// other mode. Resolvers evaluate each pool entry against it (cost via
        /// <see cref="TerrainPointsBudget.CostOf"/>) for graying, warnings and the header lines.
        /// </summary>
        public TerrainPointsBudget? PointsBudget { get; }

        [JsonConstructor]
        public PlaceOneTerrainRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            int piecesPlaced, int totalPieces, IReadOnlyList<TerrainPieceEntry> pool,
            float tableWidthInches, float tableHeightInches, TerrainPointsBudget? pointsBudget = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            PiecesPlaced = piecesPlaced;
            TotalPieces = totalPieces;
            Pool = pool;
            TableWidthInches = tableWidthInches;
            TableHeightInches = tableHeightInches;
            PointsBudget = pointsBudget;
        }

        public PlaceOneTerrainRequest(PlayerID targetPlayerID, string taskName,
            int piecesPlaced, int totalPieces, IReadOnlyList<TerrainPieceEntry> pool,
            float tableWidthInches, float tableHeightInches, TerrainPointsBudget? pointsBudget = null)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), taskName,
                   piecesPlaced, totalPieces, pool, tableWidthInches, tableHeightInches, pointsBudget)
        { }

        public Task<TerrainPlacementResult> Resolve(TerrainPlacementResult resolution) => Task.FromResult(resolution);
    }
}
