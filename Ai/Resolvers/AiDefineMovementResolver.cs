using FDG.StageResolution;
using FDG.StageResolution.Requests;

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
                : request.MaxChargeDistance - 0.001f;

            var enemyPositions = GetLiveEnemyPositions();
            if (enemyPositions.Count == 0)
                return Task.FromResult(StayInPlace(request));

            float cx = unit.ModelBindings.Average(mb => mb.GetValue().Position.x);
            float cz = unit.ModelBindings.Average(mb => mb.GetValue().Position.z);

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
            float ndx = dx / dist * step;
            float ndz = dz / dist * step;

            var entries = unit.ModelBindings.Select(mb =>
            {
                var m = mb.GetValue();
                var newPos = new Position(m.Position.x + ndx, m.Position.z + ndz);
                return new ModelMoveEntry(mb, new List<Position> { newPos });
            }).ToList();

            return Task.FromResult(entries);
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
