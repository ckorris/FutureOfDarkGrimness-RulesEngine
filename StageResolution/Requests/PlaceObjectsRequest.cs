using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class PlaceObjectsRequest<T> : IStageTaskRequest<CancellableResult<List<PlacedObjectEntry<T>>>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        /// <summary>
        /// The region models must be placed within — any <see cref="IBoundedZone"/> (a rectangular
        /// deployment zone, or a circular disembark zone, …). Carried by value (serialized polymorphically
        /// via the message layer's <c>TypeNameHandling.Auto</c>) rather than as a store reference, so the
        /// request fully describes its own placement shape. Resolvers scan <see cref="IBoundedZone.Bounds"/>
        /// and reject points outside the true shape via <see cref="IZone.IsPointWithinZone"/>.
        /// </summary>
        public IBoundedZone DeploymentZone { get; }

        public IReadOnlyList<DataBinding<T>> ModelsToPlace { get; }

        /// <summary>
        /// When &gt; 0, placed objects must end at least this far (base-edge ignored; center distance)
        /// from every live enemy model. Used by Ambush reserve arrival ("over 9" from enemies");
        /// 0 for normal deployment / Scout (zone containment is the only constraint).
        /// </summary>
        public float MinDistanceFromEnemiesInches { get; }

        /// <summary>
        /// #029: when set, the unit must come onto the table touching a table edge — at least one placed
        /// model's base must touch an edge of <see cref="DeploymentZone"/>'s bounds (the whole table, for the
        /// Aircraft off-table redeploy that uses this). See <see cref="PlacementUtilities.TouchesZoneEdge"/>.
        /// </summary>
        public bool MustTouchTableEdge { get; }

        /// <summary>
        /// When &gt; 0, each placed model must end within this many inches of the position it currently
        /// occupies — a per-model radius, not a shared zone. Used by the reposition-at-activation rules
        /// (#197: Wolfborn / Bounding / Rapid Blink, "you may place all models with this rule anywhere fully
        /// within D3\" of their position"). Unlike a move, nothing is required of the path: a placement may
        /// cross terrain and enemies, so long as the destination itself is legal. 0 = unconstrained, which is
        /// every deployment / reserve-arrival placement.
        /// <para>The start position is read from each model's live <c>Position</c>, so a resolver must
        /// validate before mutating any of them — which is already how <c>PlacedObjectEntry</c> works.</para>
        /// </summary>
        public float MaxDistanceFromStartInches { get; }

        /// <summary>
        /// Whether the resolver should offer a Back button that abandons the placement. True only for
        /// Disembark, an action the player chose from the action menu and can decline before any model is
        /// repositioned. False for placements the player cannot refuse - deployment, Scout, Ambush arrival,
        /// transport spillout - where a Cancelled reply has nowhere to return to.
        /// </summary>
        public bool AllowCancel { get; }

        [JsonConstructor]
        public PlaceObjectsRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            IBoundedZone deploymentZone, IReadOnlyList<DataBinding<T>> modelsToPlace,
            float minDistanceFromEnemiesInches = 0f, bool mustTouchTableEdge = false, bool allowCancel = false,
            float maxDistanceFromStartInches = 0f)
        {
            MaxDistanceFromStartInches = maxDistanceFromStartInches;
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            DeploymentZone = deploymentZone;
            ModelsToPlace = modelsToPlace;
            MinDistanceFromEnemiesInches = minDistanceFromEnemiesInches;
            MustTouchTableEdge = mustTouchTableEdge;
            AllowCancel = allowCancel;
        }

        public PlaceObjectsRequest(PlayerID targetPlayerID, string taskName,
            IBoundedZone deploymentZone, IReadOnlyList<DataBinding<T>> modelsToPlace,
            float minDistanceFromEnemiesInches = 0f, bool mustTouchTableEdge = false, bool allowCancel = false,
            float maxDistanceFromStartInches = 0f)
        {
            MaxDistanceFromStartInches = maxDistanceFromStartInches;
            TargetPlayerID = targetPlayerID;
            TaskName = taskName;
            DeploymentZone = deploymentZone;
            ModelsToPlace = modelsToPlace;
            MinDistanceFromEnemiesInches = minDistanceFromEnemiesInches;
            MustTouchTableEdge = mustTouchTableEdge;
            AllowCancel = allowCancel;
        }

        public Task<CancellableResult<List<PlacedObjectEntry<T>>>> Resolve(CancellableResult<List<PlacedObjectEntry<T>>> resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    // Facing (#150), when present, is the yaw facing (a unit normal) to give the placed model — set by the GUI
    // deploy resolver's rotation. Null → facing is left unchanged (its default / prior value).
    public record PlacedObjectEntry<T>(DataBinding<T> Binding, Position Position, Float2? Facing = null);
}
