using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Moves each AI unit toward the nearest living enemy.
    ///
    /// Melee/Hybrid units rush at full charge distance to close the gap.
    /// Shooting units advance at advance distance (preserving the ability to shoot afterward).
    /// </summary>
    public class AiDefineMovementResolver : IStageResolver<DefineMovementPathRequest, List<ModelMoveEntry>>
    {
        private readonly ITableState _tableState;
        private readonly PlayerID _playerID;

        public AiDefineMovementResolver(ITableState tableState, PlayerID playerID)
        {
            _tableState = tableState;
            _playerID = playerID;
        }

        public Task<List<ModelMoveEntry>> Resolve(DefineMovementPathRequest request)
        {
            var unit = request.UnitDataBinding.GetValue();
            var archetype = AiUnitClassifier.Classify(unit);

            // Shooting units advance (can still shoot); melee/hybrid rush to close distance.
            float moveDistance = archetype == AiUnitArchetype.Shooting
                ? request.MaxAdvanceDistance - 0.001f
                : request.MaxDistanceInches - 0.001f;

            var enemyPositions = GetLiveEnemyPositions();
            if (enemyPositions.Count == 0)
                return Task.FromResult(StayInPlace(request));

            // Move (and re-form) only the living models. Dead ones leave holes in the formation and stay
            // put — they're omitted from the move entries, so the cohesion check only sees living models.
            var living = unit.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0)
                return Task.FromResult(StayInPlace(request));

            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);

            Position nearest = enemyPositions
                .OrderBy(p => Dist(p.x, p.z, cx, cz))
                .First();

            float dx = nearest.x - cx;
            float dz = nearest.z - cz;
            float dist = MathF.Sqrt(dx * dx + dz * dz);

            if (dist < 0.01f)
                return Task.FromResult(StayInPlace(request));

            // Don't overshoot — stop 1" base-to-base short to avoid illegal overlap.
            float step = Math.Min(moveDistance, Math.Max(0f, dist - 1f));

            // Check terrain along the unit centroid's path before committing the step.
            var allTerrain = _tableState.Terrain.Objects.ToList();
            var unitCentroidStart = new Float2(cx, cz);
            float ndx = dx / dist;
            float ndz = dz / dist;
            var unitCentroidEnd = new Float2(cx + ndx * step, cz + ndz * step);

            bool crossesImpassible = allTerrain
                .Any(t => t.TerrainType.HasFlag(ETerrainType.Impassible)
                          && t.Shape.DoesPathIntersectZone(unitCentroidStart, unitCentroidEnd));

            if (crossesImpassible)
                return Task.FromResult(StayInPlace(request));

            bool crossesDifficult = allTerrain
                .Any(t => t.TerrainType.HasFlag(ETerrainType.Difficult)
                          && t.Shape.DoesPathIntersectZone(unitCentroidStart, unitCentroidEnd));

            if (crossesDifficult)
                step = Math.Min(step, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES - 0.001f);

            // A single living model has no cohesion to maintain — just step it toward the enemy.
            if (living.Count == 1)
            {
                var only = living[0].GetValue();
                var dest = new Position(only.Position.x + ndx * step, only.Position.z + ndz * step);
                return Task.FromResult(new List<ModelMoveEntry>
                    { new ModelMoveEntry(living[0], new List<Position> { dest }) });
            }

            // Re-pack the living models into a tight cohesive grid at the destination. A rigid translate
            // would preserve any hole a casualty left in the formation (neighbours of a dead model end up
            // >1" apart), so the move would be rejected for breaking cohesion. The grid always satisfies
            // the 1" rule. Clamp the advance so each model's re-pack move stays within budget.
            step = CohesiveFormation.ClampRepackStep(living, cx, cz, step, request.MaxDistanceInches);
            return Task.FromResult(CohesiveFormation.PackGrid(living, cx + ndx * step, cz + ndz * step));
        }

        private List<Position> GetLiveEnemyPositions()
        {
            var positions = new List<Position>();
            foreach (var unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID == _playerID) continue;
                foreach (var model in unit.Models)
                {
                    if (model is ModelData md && md.GetIsAlive() && (md.Position.x != 0f || md.Position.z != 0f))
                        positions.Add(md.Position);
                }
            }
            return positions;
        }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static List<ModelMoveEntry> StayInPlace(DefineMovementPathRequest request) =>
            request.UnitDataBinding.GetValue().ModelBindings
                .Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position }))
                .ToList();
    }
}
