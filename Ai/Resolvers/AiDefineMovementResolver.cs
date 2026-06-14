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

            var enemyFootprints = GetLiveEnemyFootprints();
            var enemyPositions = enemyFootprints.Select(f => f.Center).ToList();
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

            float ndx = dx / dist;
            float ndz = dz / dist;

            // (A) Cheap centroid pre-filter. Inflate terrain footprints by the unit's base radius (the
            // largest living model) so this matches the base-radius-aware path validator (#050) — the
            // zero-width line used to let the centroid skirt terrain a model's base would actually clip.
            var allTerrain = _tableState.Terrain.Objects.ToList();
            float baseRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);
            var unitCentroidStart = new Float2(cx, cz);
            var unitCentroidEnd = new Float2(cx + ndx * step, cz + ndz * step);

            bool crossesImpassible = allTerrain
                .Any(t => t.TerrainType.HasFlag(ETerrainType.Impassible)
                          && t.Shape.DoesPathIntersectZone(unitCentroidStart, unitCentroidEnd, baseRadius));

            if (crossesImpassible)
                return Task.FromResult(StayInPlace(request));

            bool crossesDifficult = allTerrain
                .Any(t => t.TerrainType.HasFlag(ETerrainType.Difficult)
                          && t.Shape.DoesPathIntersectZone(unitCentroidStart, unitCentroidEnd, baseRadius));

            if (crossesDifficult)
                step = Math.Min(step, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES - 0.001f);

            // (B) The centroid pre-filter can't see per-model paths, cohesion, enemy-unit crossings, or
            // charge-reach — and an invalid move makes DefinePathStage throw with no retry. So validate the
            // actual candidate against the same MovementUtilities.ValidatePaths the stage uses, backing the
            // step off until it passes (and standing still as a last resort). The AI never submits a move
            // the engine will reject.
            List<ModelMoveEntry> candidate = BuildCandidate(living, cx, cz, ndx, ndz, step, request);
            bool valid = MovementUtilities.ValidatePaths(candidate, request.MaxRushDistance,
                request.MaxDistanceInches, enemyFootprints, allTerrain, out _);

            int attempts = 0;
            while (!valid && attempts < MaxBackoffAttempts)
            {
                step *= 0.5f;
                candidate = step < MinBackoffStepInches
                    ? StayInPlace(request)
                    : BuildCandidate(living, cx, cz, ndx, ndz, step, request);
                valid = MovementUtilities.ValidatePaths(candidate, request.MaxRushDistance,
                    request.MaxDistanceInches, enemyFootprints, allTerrain, out _);
                attempts++;
            }

            if (!valid)
                candidate = StayInPlace(request);

            return Task.FromResult(candidate);
        }

        //Backoff bounds for (B): halve the step up to this many times before giving up and standing still.
        private const int MaxBackoffAttempts = 6;
        private const float MinBackoffStepInches = 0.05f;

        // Builds the move the AI wants for a given step: a single living model steps straight toward the
        // enemy; multiple models re-pack into a tight cohesive grid at the destination (a rigid translate
        // would preserve holes left by casualties and break the 1" cohesion rule). Clamps the re-pack so
        // each model's move stays within budget.
        private static List<ModelMoveEntry> BuildCandidate(List<DataBinding<ModelData>> living,
            float cx, float cz, float ndx, float ndz, float step, DefineMovementPathRequest request)
        {
            if (step <= 0f) return StayInPlace(request);

            if (living.Count == 1)
            {
                var only = living[0].GetValue();
                var dest = new Position(only.Position.x + ndx * step, only.Position.z + ndz * step);
                return new List<ModelMoveEntry> { new ModelMoveEntry(living[0], new List<Position> { dest }) };
            }

            float clamped = CohesiveFormation.ClampRepackStep(living, cx, cz, step, request.MaxDistanceInches);
            return CohesiveFormation.PackGrid(living, cx + ndx * clamped, cz + ndz * clamped);
        }

        // Living enemy model footprints, tagged with a per-unit key (so the validator can tell which models
        // share an enemy unit). Mirrors MovementUtilities.GetEnemyModelFootprints but reads from ITableState.
        private List<EnemyModelFootprint> GetLiveEnemyFootprints()
        {
            var footprints = new List<EnemyModelFootprint>();
            int unitKey = 0;
            foreach (var unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID == _playerID) continue;
                bool anyLiving = false;
                foreach (var model in unit.Models)
                {
                    if (model is ModelData md && md.GetIsAlive() && (md.Position.x != 0f || md.Position.z != 0f))
                    {
                        footprints.Add(new EnemyModelFootprint(md.Position, md.BaseRadiusInches, unitKey));
                        anyLiving = true;
                    }
                }
                if (anyLiving) unitKey++;
            }
            return footprints;
        }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        // "Stay put", but reform the living models into a cohesive grid at their current centroid rather
        // than literally standing still. After casualties the survivors can be >1" apart (the dead left
        // gaps between them), so a literal stay would submit a cohesion-breaking move that DefinePathStage
        // rejects with no retry — crashing the game. Reforming in place keeps this fallback always valid.
        // 0–1 living models can't break cohesion, so they keep their exact positions.
        private static List<ModelMoveEntry> StayInPlace(DefineMovementPathRequest request)
        {
            List<DataBinding<ModelData>> living = request.UnitDataBinding.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();

            if (living.Count <= 1)
            {
                return request.UnitDataBinding.GetValue().ModelBindings
                    .Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position }))
                    .ToList();
            }

            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);
            return CohesiveFormation.PackGrid(living, cx, cz);
        }
    }
}
