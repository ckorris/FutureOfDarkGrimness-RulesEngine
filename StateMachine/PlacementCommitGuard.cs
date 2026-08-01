using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #284 (was #282 pre-reconciliation-27) - the commit-time overlap check for mandatory model placements. The placement stages
    /// (deploy, re-deploy, Scout, Ambush arrival, transport spillout) used to commit whatever a
    /// resolver returned - <c>DeploymentSelection.ValidatePosition</c> checks only zone containment -
    /// so any upstream failure (the YellowDeployedOverGreen save: a second AI block deployed
    /// concentric with an ally it somehow could not see) silently corrupted the board.
    ///
    /// <para>This guard runs at the commit seam: if the returned placements interpenetrate any
    /// on-table model deeper than <see cref="OverlapToleranceInches"/>, it warns in the game log
    /// (visibility is half the point - a recurrence becomes a diagnosis, not a mystery) and
    /// re-places the unit through <see cref="AiPlaceObjectsResolver{T}"/>, whose sweep reads a
    /// FRESH occupancy view and honours the request's own zone / enemy-distance / edge constraints.
    /// If the zone genuinely has no clear spot the sweep returns its least-overlapping centre and
    /// the guard commits that, with a second warning.</para>
    /// </summary>
    public static class PlacementCommitGuard
    {
        /// <summary>Interpenetration deeper than this counts as a violation. Legal tight packs can
        /// brush zero by float drift; the margin matches the codebase's preview-clamp discipline.</summary>
        public const float OverlapToleranceInches = 0.01f;

        /// <summary>
        /// <see cref="PlacementRequesting.RequestMandatoryPlacement{T}"/> plus the commit-time
        /// overlap check - the placement stages call this instead of the raw request.
        /// </summary>
        public static async Task<List<PlacedObjectEntry<ModelData>>> RequestClearPlacement(
            IGameContext gameContext, PlaceObjectsRequest<ModelData> request)
        {
            List<PlacedObjectEntry<ModelData>> placements = await PlacementRequesting
                .RequestMandatoryPlacement(gameContext.PlayerRequester, request);
            return await EnsureClear(gameContext, request, placements);
        }

        /// <summary>
        /// <see cref="RequestClearPlacement"/> for a placement the player may abandon (#308: deployment,
        /// where backing out returns to the unit list). Returns null when the resolver cancels — the caller
        /// owns the undo, since only it knows what picking the unit already changed.
        /// <para>The overlap guard still runs on a committed placement: a cancel is the player declining,
        /// not a reason to skip the check on the placement they DID make.</para>
        /// </summary>
        public static async Task<List<PlacedObjectEntry<ModelData>>?> RequestClearPlacementOrCancel(
            IGameContext gameContext, PlaceObjectsRequest<ModelData> request)
        {
            CancellableResult<List<PlacedObjectEntry<ModelData>>> result = await gameContext.PlayerRequester
                .RequestDecision<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(request);

            if (result is not Selected<List<PlacedObjectEntry<ModelData>>> selected) return null;

            return await EnsureClear(gameContext, request, selected.Value);
        }

        /// <summary>The placements to commit: the originals when clear, or a re-placed set when
        /// they overlap on-table models (logged either way it goes).</summary>
        public static async Task<List<PlacedObjectEntry<ModelData>>> EnsureClear(
            IGameContext gameContext, PlaceObjectsRequest<ModelData> request,
            List<PlacedObjectEntry<ModelData>> placements)
        {
            if (placements.Count == 0) return placements;

            (string blocker, float depth) = WorstOverlap(gameContext.TableState, request, placements);
            if (depth <= OverlapToleranceInches) return placements;

            string unitName = PlacingUnitName(gameContext.TableState, request);
            gameContext.Log($"WARNING: {unitName} placement overlapped {blocker} " +
                $"(worst {depth:F2}in) - re-placing on a clear spot.");

            var repairResolver = new AiPlaceObjectsResolver<ModelData>(gameContext.TableState);
            CancellableResult<List<PlacedObjectEntry<ModelData>>> repaired =
                await repairResolver.Resolve(request);
            // The AI resolver never cancels a placement; if that contract ever breaks, committing
            // the original (logged) overlap is still better than dropping the unit off the table.
            if (repaired is not Selected<List<PlacedObjectEntry<ModelData>>> selected) return placements;

            (string stillBlocker, float stillDepth) =
                WorstOverlap(gameContext.TableState, request, selected.Value);
            if (stillDepth > OverlapToleranceInches)
            {
                gameContext.Log($"WARNING: no clear spot for {unitName}; committing the " +
                    $"least-overlapping placement ({stillDepth:F2}in against {stillBlocker}).");
            }
            return selected.Value;
        }

        // Deepest interpenetration between the proposed placements and any OTHER unit's living,
        // on-table model, measured by true oriented footprints. The placing unit's own models are
        // excluded: they are the ones moving (a re-deploying unit still stands at its old spot),
        // and intra-formation legality is the resolver's own contract.
        private static (string blocker, float depth) WorstOverlap(ITableState tableState,
            PlaceObjectsRequest<ModelData> request, List<PlacedObjectEntry<ModelData>> placements)
        {
            var self = new HashSet<IModel>(ReferenceEqualityComparer.Instance);
            foreach (DataBinding<ModelData> binding in request.ModelsToPlace)
                self.Add(binding.GetValue());

            float worst = 0f;
            string blocker = "another unit";
            foreach (IUnit unit in tableState.Units.Objects)
            {
                foreach (IModel other in unit.Models)
                {
                    if (self.Contains(other)) continue;
                    if (!other.GetIsAlive()) continue;
                    Position otherPos = other.Position;
                    if (otherPos.x == 0f && otherPos.z == 0f) continue; // unplaced / in reserve

                    foreach (PlacedObjectEntry<ModelData> placed in placements)
                    {
                        ModelData placing = placed.Binding.GetValue();
                        Float2 facing = placed.Facing ?? placing.Facing;
                        float gap = BaseShapeGeometry.SurfaceGap2D(placing.BaseShape, placed.Position,
                            facing, other.BaseShape, otherPos, other.Facing);
                        if (-gap > worst) { worst = -gap; blocker = unit.Name; }
                    }
                }
            }
            return (blocker, worst);
        }

        private static string PlacingUnitName(ITableState tableState, PlaceObjectsRequest<ModelData> request)
        {
            if (request.ModelsToPlace.Count > 0)
            {
                ModelData first = request.ModelsToPlace[0].GetValue();
                foreach (IUnit unit in tableState.Units.Objects)
                    foreach (IModel model in unit.Models)
                        if (ReferenceEquals(model, first))
                            return unit.Name;
            }
            return $"'{request.TaskName}'";
        }
    }
}
