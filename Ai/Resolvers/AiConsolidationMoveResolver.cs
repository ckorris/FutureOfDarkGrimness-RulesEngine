using FDG.Ai.Tactician;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Wipeout: shuffle up to 3" toward the nearest living enemy model.
    /// Disengage: step 1" directly away from the defender unit's center, satisfying the disengage rule.
    /// Skips (stays in place) when there's no sensible target.
    /// </summary>
    public class AiConsolidationMoveResolver : IStageResolver<ConsolidationMoveRequest, List<ModelMoveEntry>>
    {
        private readonly ITableState _tableState;
        private readonly PlayerID _playerID;

        public AiConsolidationMoveResolver(ITableState tableState, PlayerID playerID)
        {
            _tableState = tableState;
            _playerID = playerID;
        }

        public Task<List<ModelMoveEntry>> Resolve(ConsolidationMoveRequest request)
        {
            var unit = request.UnitDataBinding.GetValue();
            var aliveModels = unit.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (aliveModels.Count == 0)
                return Task.FromResult(StayInPlace(aliveModels));

            float cx = aliveModels.Average(mb => mb.GetValue().Position.x);
            float cz = aliveModels.Average(mb => mb.GetValue().Position.z);

            List<EnemyModelFootprint> footprints = GetLiveEnemyFootprints();
            // #205: friendly obstacles the consolidation may not end stacked on (fed to the same lenient
            // validator ConsolidateStage runs, so the AI backs off rather than being rejected downstream).
            List<EnemyModelFootprint> friendlyFootprints =
                MovementPlanner.LiveFriendlyFootprints(_tableState, _playerID, unit.ID);
            IEnumerable<ITerrain> terrain = _tableState.Terrain.Objects;

            // #159: a unit left out of coherency by a mid-unit casualty can't consolidate as a rigid delta (a
            // rigid translate preserves the hole, and ConsolidateStage would reject the still-broken result).
            // Re-form the survivors toward their centroid within the cap first — pulling them as close back to
            // coherency as the 1-3" consolidation allows (the "attempt to bring them together"). Validated
            // against the same lenient check ConsolidateStage runs, so it's never rejected downstream.
            if (!CohesiveFormation.IsCohesive(aliveModels))
            {
                List<ModelMoveEntry> reform = CohesiveFormation.ReformTowardWithinCap(
                    aliveModels, cx, cz, request.MaxDistanceInches - 0.001f);
                if (MovementUtilities.ValidateConsolidationPaths(reform, request.MaxDistanceInches, footprints,
                        request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friendlyFootprints))
                    return Task.FromResult(reform);
                // else fall through — a rigid step (which never worsens coherency) or the hold below stays valid.
            }

            (float dx, float dz)? target = request.Reason == EConsolidationReason.Disengage
                ? GetDisengageDelta(cx, cz)
                : GetWipeoutDelta(cx, cz);

            if (target == null)
                return Task.FromResult(StayInPlace(aliveModels));

            float dist = MathF.Sqrt(target.Value.dx * target.Value.dx + target.Value.dz * target.Value.dz);
            if (dist < 0.01f)
                return Task.FromResult(StayInPlace(aliveModels));

            float dirX = target.Value.dx / dist;
            float dirZ = target.Value.dz / dist;
            float maxStep = MathF.Min(request.MaxDistanceInches - 0.001f, dist);

            // Back the step off until the same validator ConsolidateStage runs accepts the move, standing
            // still as a last resort — so the AI never submits a consolidation the stage will reject (e.g. one
            // that passes through or ends stacked on a different enemy unit). #090.
            for (float step = maxStep; step >= 0.01f; step *= 0.5f)
            {
                List<ModelMoveEntry> candidate = BuildDeltaMove(aliveModels, dirX, dirZ, step);
                if (MovementUtilities.ValidateConsolidationPaths(candidate, request.MaxDistanceInches, footprints,
                        request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friendlyFootprints))
                    return Task.FromResult(candidate);
            }
            return Task.FromResult(StayInPlace(aliveModels));
        }

        // Build a unit-wide delta move of `step` inches along (dirX,dirZ), scaled in to keep every base on
        // the table. All models share the delta, so cohesion is preserved.
        private static List<ModelMoveEntry> BuildDeltaMove(List<DataBinding<ModelData>> aliveModels,
            float dirX, float dirZ, float step)
        {
            float ndx = dirX * step;
            float ndz = dirZ * step;
            float scale = MaxInBoundsScale(aliveModels, ndx, ndz);
            ndx *= scale;
            ndz *= scale;
            return aliveModels.Select(mb =>
            {
                var m = mb.GetValue();
                return new ModelMoveEntry(mb,
                    new List<Position> { new Position(m.Position.x + ndx, m.Position.z + ndz) });
            }).ToList();
        }

        // Living enemy model footprints, tagged per-unit. Mirrors MovementUtilities.GetEnemyModelFootprints
        // but reads from ITableState (the AI resolver's view).
        private List<EnemyModelFootprint> GetLiveEnemyFootprints()
        {
            var footprints = new List<EnemyModelFootprint>();
            int unitKey = 0;
            foreach (var unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID == _playerID) continue;
                bool uncontactable = FDG.Rules.Dispatch.AircraftRules.IsAircraft(unit); // #029
                bool anyLiving = false;
                foreach (var model in unit.Models)
                {
                    if (model is ModelData md && md.GetIsAlive() && (md.Position.x != 0f || md.Position.z != 0f))
                    {
                        footprints.Add(new EnemyModelFootprint(md.Position, md.BaseRadiusInches, unitKey, uncontactable, md.BaseShape, md.Facing));
                        anyLiving = true;
                    }
                }
                if (anyLiving) unitKey++;
            }
            return footprints;
        }

        private (float dx, float dz)? GetWipeoutDelta(float cx, float cz)
        {
            Position? nearest = null;
            float nearestSq = float.MaxValue;
            foreach (var u in _tableState.Units.Objects)
            {
                if (u.PlayerID == _playerID) continue;
                foreach (var m in u.Models)
                {
                    if (!m.GetIsAlive()) continue;
                    float dx = m.Position.x - cx, dz = m.Position.z - cz;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < nearestSq) { nearestSq = d2; nearest = m.Position; }
                }
            }
            return nearest is { } p ? (p.x - cx, p.z - cz) : null;
        }

        // Disengage: pull away from the average position of the nearest enemy unit's alive models.
        // Disengage only fires when at least one enemy is in melee, so there's always a "back" direction.
        private (float dx, float dz)? GetDisengageDelta(float cx, float cz)
        {
            Position? nearest = null;
            float nearestSq = float.MaxValue;
            foreach (var u in _tableState.Units.Objects)
            {
                if (u.PlayerID == _playerID) continue;
                foreach (var m in u.Models)
                {
                    if (!m.GetIsAlive()) continue;
                    float dx = m.Position.x - cx, dz = m.Position.z - cz;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < nearestSq) { nearestSq = d2; nearest = m.Position; }
                }
            }
            return nearest is { } p ? (cx - p.x, cz - p.z) : null;
        }

        // Largest t in [0,1] such that for every model, position + t*(dx,dz) keeps the base inside the table.
        private static float MaxInBoundsScale(List<DataBinding<ModelData>> aliveModels, float dx, float dz)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float t = 1f;
            foreach (var mb in aliveModels)
            {
                var m = mb.GetValue();
                // Circumscribing radius so a rotated rectangular base can't poke a corner off the table
                // (rotation-safe, matching ForcedAircraftMove / TouchesZoneEdge). Circle: unchanged.
                float r = m.BaseShape.CircumscribedRadiusInches;
                t = MathF.Min(t, AxisLimit(m.Position.x, dx, r, w - r));
                t = MathF.Min(t, AxisLimit(m.Position.z, dz, r, h - r));
            }
            return MathF.Max(0f, t);
        }

        private static float AxisLimit(float p, float d, float lo, float hi)
        {
            if (d > 0f && p + d > hi) return (hi - p) / d;
            if (d < 0f && p + d < lo) return (lo - p) / d;
            return 1f;
        }

        private static List<ModelMoveEntry> StayInPlace(List<DataBinding<ModelData>> aliveModels) =>
            aliveModels.Select(mb => new ModelMoveEntry(mb, new List<Position>())).ToList();
    }
}
