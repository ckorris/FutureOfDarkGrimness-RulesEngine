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
            if (enemyFootprints.Count == 0)
                return Task.FromResult(StayInPlace(request));

            // Move (and re-form) only the living models. Dead ones leave holes in the formation and stay
            // put — they're omitted from the move entries, so the cohesion check only sees living models.
            var living = unit.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0)
                return Task.FromResult(StayInPlace(request));

            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);

            EnemyModelFootprint nearest = enemyFootprints
                .OrderBy(f => Dist(f.Center.x, f.Center.z, cx, cz))
                .First();

            float dx = nearest.Center.x - cx;
            float dz = nearest.Center.z - cz;
            float dist = MathF.Sqrt(dx * dx + dz * dz);

            if (dist < 0.01f)
                return Task.FromResult(StayInPlace(request));

            float ndx = dx / dist;
            float ndz = dz / dist;

            // Aim for an explicit end gap rather than a blind "1" short of centre" (which is actually base
            // overlap). A melee/hybrid unit that can reach charges into base contact; everyone else advances
            // to the standoff line. Because PackGrid translates the whole formation, the nearest model's gap
            // responds ~1:1 to the centroid step, so a couple of measure-and-correct passes land it on target.
            float leadRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);
            float contactDistance = leadRadius + nearest.BaseRadiusInches;
            bool wantsCharge = archetype != AiUnitArchetype.Shooting
                && (dist - contactDistance) <= moveDistance + 0.05f;
            float targetGap = wantsCharge
                ? ChargeContactTargetGapInches
                : GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES + StandoffTargetMarginInches;

            float step = Math.Clamp(dist - contactDistance - targetGap, 0f, moveDistance);
            for (int i = 0; i < TargetRefineIterations; i++)
            {
                var probe = BuildCandidate(living, cx, cz, ndx, ndz, step, request);
                float achievedGap = MinEnemyGap(probe, enemyFootprints);
                float error = achievedGap - targetGap;
                if (Math.Abs(error) <= TargetGapToleranceInches) break;
                step = Math.Clamp(step + error, 0f, moveDistance);
            }

            // (A) Terrain-aware centroid steering. Inflate terrain footprints by the unit's base radius (the
            // largest living model) so this matches the base-radius-aware path validator (#050).
            var allTerrain = _tableState.Terrain.Objects.ToList();
            float baseRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);

            // If the straight line to the target crosses impassible terrain, the unit used to give up and
            // stand still every turn — so a unit deployed behind a building never advanced. Instead try
            // angling left/right to skirt it (a cheap "go around", not full pathfinding) and take the clear
            // direction that lands closest to the target. If nothing skirts at full step, fall through to the
            // per-model backoff below, which shortens the move so the unit at least advances part-way.
            // Flying ignores impassible terrain (it flies over), so don't bother skirting — fly straight at
            // the target (the path validator below also waives the impassible block for this unit).
            if (!request.IgnoresImpassibleTerrain
                && CentroidCrossesTerrain(allTerrain, ETerrainType.Impassible, cx, cz, ndx, ndz, step, baseRadius)
                && TryFindSkirtingDirection(allTerrain, cx, cz, ndx, ndz, step, baseRadius,
                    nearest.Center, out float skirtDx, out float skirtDz))
            {
                ndx = skirtDx;
                ndz = skirtDz;
            }

            // Strider waives the difficult-terrain cap, so don't pre-clamp the AI's step to it (the path
            // validator below also skips the cap for this unit, so a clamp here would only under-move).
            if (!request.IgnoresDifficultTerrain
                && CentroidCrossesTerrain(allTerrain, ETerrainType.Difficult, cx, cz, ndx, ndz, step, baseRadius))
                step = Math.Min(step, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES - 0.001f);

            // (B) The centroid pre-filter can't see per-model paths, cohesion, enemy-unit crossings, or
            // charge-reach — and an invalid move makes DefinePathStage throw with no retry. So validate the
            // actual candidate against the same MovementUtilities.ValidatePaths the stage uses, backing the
            // step off until it passes (and standing still as a last resort). The AI never submits a move
            // the engine will reject.
            List<ModelMoveEntry> candidate = BuildCandidate(living, cx, cz, ndx, ndz, step, request);
            bool valid = MovementUtilities.ValidatePaths(candidate, request.MaxRushDistance,
                request.MaxDistanceInches, enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, allTerrain, out _);

            int attempts = 0;
            while (!valid && attempts < MaxBackoffAttempts)
            {
                step *= 0.5f;
                candidate = step < MinBackoffStepInches
                    ? StayInPlace(request)
                    : BuildCandidate(living, cx, cz, ndx, ndz, step, request);
                valid = MovementUtilities.ValidatePaths(candidate, request.MaxRushDistance,
                    request.MaxDistanceInches, enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, allTerrain, out _);
                attempts++;
            }

            if (!valid)
            {
                // Reform in place to close any casualty gaps...
                candidate = StayInPlace(request);
                valid = MovementUtilities.ValidatePaths(candidate, request.MaxRushDistance,
                    request.MaxDistanceInches, enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, allTerrain, out _);

                // ...but if even that is rejected (a unit intermingled with enemies can't re-pack without
                // a model crossing an enemy base), hold exact positions — zero-length paths can't move
                // through anything, so this is always move-through-valid.
                if (!valid)
                    candidate = HoldExactPositions(living);
            }

            return Task.FromResult(candidate);
        }

        private static List<ModelMoveEntry> HoldExactPositions(List<DataBinding<ModelData>> living)
            => living.Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position })).ToList();

        //Backoff bounds for (B): halve the step up to this many times before giving up and standing still.
        private const int MaxBackoffAttempts = 6;
        private const float MinBackoffStepInches = 0.05f;

        //Aim tuning. A charge targets just inside base contact (still < the validator's contact tolerance,
        //so it reads as engaged, not as standoff-band loitering); an advance targets just past the standoff
        //line. A few measure-and-correct passes converge thanks to the ~1:1 step↔gap response.
        private const float ChargeContactTargetGapInches = 0.05f;
        private const float StandoffTargetMarginInches = 0.05f;
        private const float TargetGapToleranceInches = 0.05f;
        private const int TargetRefineIterations = 3;

        // Smallest base-to-base gap between any moving model's end position and any enemy model — i.e. how
        // close the move actually gets, the quantity the move-through / standoff validator keys on.
        private static float MinEnemyGap(List<ModelMoveEntry> moves, List<EnemyModelFootprint> enemies)
        {
            float min = float.PositiveInfinity;
            foreach (var move in moves)
            {
                if (move.Positions.Count == 0) continue;
                Position end = move.Positions[move.Positions.Count - 1];
                float r = move.Model.GetValue().BaseRadiusInches;
                foreach (var enemy in enemies)
                {
                    float gap = Position.GetDistance2D(end, enemy.Center) - r - enemy.BaseRadiusInches;
                    if (gap < min) min = gap;
                }
            }
            return min;
        }

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
                bool uncontactable = FDG.Rules.Dispatch.AircraftRules.IsAircraft(unit); // #029
                bool anyLiving = false;
                foreach (var model in unit.Models)
                {
                    if (model is ModelData md && md.GetIsAlive() && (md.Position.x != 0f || md.Position.z != 0f))
                    {
                        footprints.Add(new EnemyModelFootprint(md.Position, md.BaseRadiusInches, unitKey, uncontactable));
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

        // Angle offsets (degrees, both sides) tried when the straight path is blocked, widening outward.
        private static readonly float[] SkirtAngleOffsetsDegrees =
            { 20f, -20f, 40f, -40f, 60f, -60f, 80f, -80f, 100f, -100f };

        // True if the unit's centroid path (inflated by its base radius) crosses terrain of the given type.
        private static bool CentroidCrossesTerrain(List<ITerrain> terrain, ETerrainType type,
            float cx, float cz, float ndx, float ndz, float step, float baseRadius)
        {
            var start = new Float2(cx, cz);
            var end = new Float2(cx + ndx * step, cz + ndz * step);
            return terrain.Any(t => t.TerrainType.HasFlag(type)
                && t.Shape.DoesPathIntersectZone(start, end, baseRadius));
        }

        // Tries fixed angle offsets either side of the straight direction; returns the impassible-clear one
        // whose full-step end-point lands closest to the target. False when none of the tried angles is clear.
        private static bool TryFindSkirtingDirection(List<ITerrain> terrain, float cx, float cz,
            float ndx, float ndz, float step, float baseRadius, Position target, out float outDx, out float outDz)
        {
            outDx = ndx;
            outDz = ndz;
            float baseAngle = MathF.Atan2(ndz, ndx);
            float bestDist = float.PositiveInfinity;
            bool found = false;

            foreach (float deg in SkirtAngleOffsetsDegrees)
            {
                float a = baseAngle + deg * (MathF.PI / 180f);
                float dx = MathF.Cos(a), dz = MathF.Sin(a);
                if (CentroidCrossesTerrain(terrain, ETerrainType.Impassible, cx, cz, dx, dz, step, baseRadius))
                    continue;

                float endDist = Dist(cx + dx * step, cz + dz * step, target.x, target.z);
                if (endDist < bestDist)
                {
                    bestDist = endDist;
                    outDx = dx;
                    outDz = dz;
                    found = true;
                }
            }

            return found;
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
