using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Shared movement-construction primitives (#191 A3a): candidate building (formation re-pack at a
    /// translated centroid), gap-targeted step refinement, and the validate-with-backoff ladder that
    /// guarantees a submitted move is one the engine accepts (plan G3 - an invalid move makes
    /// DefinePathStage throw with no retry).
    /// <para>
    /// Extracted verbatim from AiDefineMovementResolver so the solo-rules bot and the Tactician's
    /// macro-action generator (A3c) drive the same machinery. The solo-rules bot's BEHAVIOR is pinned
    /// unchanged (plan D1): its resolver tests plus the 200-game benchmark outcome hashes
    /// (B05AA1D810364C6B / F4318EF0D91161F5, recorded in the #191 ledger) must survive this move.
    /// </para>
    /// </summary>
    public static class MovementPlanner
    {
        /// <summary>
        /// Destination layout for a multi-model candidate. <see cref="Grid"/> is CohesiveFormation's
        /// tight block (the solo-rules shape). <see cref="Line"/> is a thin rank spread along a given
        /// axis - what M8 Block(e, asset) wants for a barrier (Appendix A note: a block formation
        /// cannot wall a lane; a line can).
        /// </summary>
        public enum EFormation { Grid, Line }

        // Backoff bounds: halve the step up to this many times before giving up and standing still.
        public const int MaxBackoffAttempts = 6;
        public const float MinBackoffStepInches = 0.05f;

        // Aim tuning. A charge targets just inside base contact (still < the validator's contact
        // tolerance, so it reads as engaged, not standoff-band loitering); an advance targets just past
        // the standoff line. A few measure-and-correct passes converge thanks to the ~1:1 step<->gap
        // response of a translated formation.
        public const float ChargeContactTargetGapInches = 0.05f;
        public const float StandoffTargetMarginInches = 0.05f;
        public const float TargetGapToleranceInches = 0.05f;
        public const int TargetRefineIterations = 3;

        /// <summary>
        /// The move candidate for a given step along (<paramref name="ndx"/>, <paramref name="ndz"/>):
        /// a single living model steps straight; multiple models re-pack into a cohesive formation at
        /// the translated centroid (a rigid translate would preserve casualty holes and break the 1"
        /// rule), the re-pack clamped so each model's move stays within <paramref name="maxDistanceInches"/>.
        /// A non-positive step degrades to <see cref="StayInPlace"/> (which is why the unit binding is
        /// a parameter - that fallback includes dead models' zero-length paths when 0-1 live).
        /// </summary>
        public static List<ModelMoveEntry> BuildCandidate(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living,
            float cx, float cz, float ndx, float ndz, float step, float maxDistanceInches,
            EFormation formation = EFormation.Grid)
        {
            if (step <= 0f) return StayInPlace(unit);

            if (living.Count == 1)
            {
                var only = living[0].GetValue();
                var dest = new Position(only.Position.x + ndx * step, only.Position.z + ndz * step);
                return new List<ModelMoveEntry> { new ModelMoveEntry(living[0], new List<Position> { dest }) };
            }

            float clamped = CohesiveFormation.ClampRepackStep(living, cx, cz, step, maxDistanceInches);
            float destX = cx + ndx * clamped;
            float destZ = cz + ndz * clamped;
            return formation == EFormation.Line
                // A barrier line runs PERPENDICULAR to the move direction (across the lane being walled).
                ? PackLine(living, destX, destZ, -ndz, ndx)
                : CohesiveFormation.PackGrid(living, destX, destZ);
        }

        /// <summary>
        /// Measure-and-correct refinement toward a target end gap: probes Grid candidates and nudges the
        /// step until the closest moving model's base-to-base gap to any enemy is within tolerance of
        /// <paramref name="targetGap"/> (or iterations run out). Returns the refined step.
        /// </summary>
        public static float RefineStepTowardGap(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living,
            float cx, float cz, float ndx, float ndz, float initialStep, float maxStep,
            float targetGap, List<EnemyModelFootprint> enemies, float maxDistanceInches)
        {
            float step = initialStep;
            for (int i = 0; i < TargetRefineIterations; i++)
            {
                var probe = BuildCandidate(unit, living, cx, cz, ndx, ndz, step, maxDistanceInches);
                float achievedGap = MinEnemyGap(probe, enemies);
                float error = achievedGap - targetGap;
                if (Math.Abs(error) <= TargetGapToleranceInches) break;
                step = Math.Clamp(step + error, 0f, maxStep);
            }
            return step;
        }

        /// <summary>
        /// Smallest base-to-base gap between any moving model's end position and any enemy model - how
        /// close the move actually gets, the quantity the move-through / standoff validator keys on.
        /// Shape- and facing-aware (#150).
        /// </summary>
        public static float MinEnemyGap(List<ModelMoveEntry> moves, List<EnemyModelFootprint> enemies)
        {
            float min = float.PositiveInfinity;
            foreach (var move in moves)
            {
                if (move.Positions.Count == 0) continue;
                Position end = move.Positions[move.Positions.Count - 1];
                ModelData m = move.Model.GetValue();
                foreach (var enemy in enemies)
                {
                    float gap = BaseShapeGeometry.SurfaceGap2D(m.BaseShape, end, m.Facing,
                        enemy.BaseShape, enemy.Center, enemy.Facing);
                    if (gap < min) min = gap;
                }
            }
            return min;
        }

        /// <summary>
        /// The G3 ladder: validate the candidate with the same MovementUtilities.ValidatePaths the stage
        /// uses, halving the step until it passes; then reform-in-place; then hold exact positions
        /// (zero-length paths can't move through anything, so the last resort is always valid). The
        /// returned move is one the engine will accept.
        /// </summary>
        public static List<ModelMoveEntry> ValidateWithBackoff(
            Func<float, List<ModelMoveEntry>> candidateAt, float initialStep,
            DataBinding<UnitData> unit, List<DataBinding<ModelData>> living,
            Func<ModelMoveEntry, ModelMoveBudget> budgetFor, List<EnemyModelFootprint> enemies,
            bool canMoveThroughEnemies, bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            List<ITerrain> terrain)
        {
            float step = initialStep;
            List<ModelMoveEntry> candidate = candidateAt(step);
            bool valid = MovementUtilities.ValidatePaths(candidate, budgetFor,
                enemies, canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain, out _);

            int attempts = 0;
            while (!valid && attempts < MaxBackoffAttempts)
            {
                step *= 0.5f;
                candidate = step < MinBackoffStepInches
                    ? StayInPlace(unit)
                    : candidateAt(step);
                valid = MovementUtilities.ValidatePaths(candidate, budgetFor,
                    enemies, canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain, out _);
                attempts++;
            }

            if (!valid)
            {
                // Reform in place to close any casualty gaps...
                candidate = StayInPlace(unit);
                valid = MovementUtilities.ValidatePaths(candidate, budgetFor,
                    enemies, canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain, out _);

                // ...but if even that is rejected (a unit intermingled with enemies can't re-pack without
                // a model crossing an enemy base), hold exact positions.
                if (!valid)
                    candidate = HoldExactPositions(living);
            }

            return candidate;
        }

        /// <summary>
        /// "Stay put", but reform the living models into a cohesive grid at their current centroid: after
        /// casualties the survivors can be >1" apart, so a literal stay would submit a cohesion-breaking
        /// move that DefinePathStage rejects with no retry. 0-1 living models keep exact positions (dead
        /// ones included in the entries, as the stage expects a path per model there).
        /// </summary>
        public static List<ModelMoveEntry> StayInPlace(DataBinding<UnitData> unit)
        {
            List<DataBinding<ModelData>> living = unit.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();

            if (living.Count <= 1)
            {
                return unit.GetValue().ModelBindings
                    .Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position }))
                    .ToList();
            }

            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);
            return CohesiveFormation.PackGrid(living, cx, cz);
        }

        /// <summary>
        /// A path-following candidate (#191 A3b): the unit travels <paramref name="arcLengthInches"/>
        /// along <paramref name="path"/>, every model sharing the path's interior waypoints (so the
        /// whole unit funnels through corridors) and fanning out into the formation at the endpoint.
        /// The arc length is the ladder's backoff knob, exactly like the straight candidate's step.
        /// </summary>
        public static List<ModelMoveEntry> BuildPathCandidate(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, List<Position> path, float arcLengthInches,
            IReadOnlyList<ITerrain> terrain, float baseRadiusInches, float maxDistanceInches,
            EFormation formation = EFormation.Grid)
        {
            if (arcLengthInches <= 0f || path.Count < 2) return StayInPlace(unit);

            (Position endpoint, List<Position> passed, _) =
                GridPathfinder.AdvanceAlongPath(path, arcLengthInches, terrain, baseRadiusInches);

            Position previous = passed.Count > 0 ? passed[^1] : path[0];
            float dirX = endpoint.x - previous.x;
            float dirZ = endpoint.z - previous.z;
            float length = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
            (dirX, dirZ) = length < 1e-6f ? (1f, 0f) : (dirX / length, dirZ / length);

            List<ModelMoveEntry> destinations;
            if (living.Count == 1)
            {
                destinations = new List<ModelMoveEntry>
                    { new ModelMoveEntry(living[0], new List<Position> { endpoint }) };
            }
            else
            {
                destinations = formation == EFormation.Line
                    ? PackLine(living, endpoint.x, endpoint.z, -dirZ, dirX)
                    : CohesiveFormation.PackGrid(living, endpoint.x, endpoint.z);
            }

            if (passed.Count == 0) return destinations;
            return destinations
                .Select(entry => new ModelMoveEntry(entry.Model,
                    passed.Concat(entry.Positions).ToList()))
                .ToList();
        }

        /// <summary>
        /// The A3b composition: pathfind around impassible terrain toward <paramref name="goal"/>,
        /// advance up to the move budget (applying the engine's 6" whole-move cap when the route
        /// crosses difficult ground), and hand the result through the G3 ladder - the returned move
        /// is always one the engine accepts. Falls back to the straight line when no route exists
        /// (the ladder then shortens it), or when the unit flies over terrain anyway.
        /// </summary>
        public static List<ModelMoveEntry> PlanMoveToward(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, ITableState tableState, Position goal,
            float moveBudgetInches, float maxDistanceInches,
            Func<ModelMoveEntry, ModelMoveBudget> budgetFor,
            bool canMoveThroughEnemies, bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            EFormation formation = EFormation.Grid)
        {
            var terrain = tableState.Terrain.Objects.ToList();
            var enemies = LiveEnemyFootprints(tableState, unit.GetValue().PlayerID);
            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);
            var start = new Position(cx, cz);
            float baseRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);

            List<Position>? path = null;
            if (!ignoresImpassibleTerrain)
            {
                TerrainGrid grid = TerrainGrid.Build(terrain, baseRadius);
                path = GridPathfinder.FindPath(grid, terrain, start, goal, baseRadius);
            }
            path ??= new List<Position> { start, goal };

            float budget = moveBudgetInches;
            (_, _, bool crossesDifficult) =
                GridPathfinder.AdvanceAlongPath(path, budget, terrain, baseRadius);
            if (crossesDifficult && !ignoresDifficultTerrain)
                budget = Math.Min(budget, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES - 0.001f);

            return ValidateWithBackoff(
                arc => BuildPathCandidate(unit, living, path, arc, terrain, baseRadius,
                    maxDistanceInches, formation),
                budget, unit, living, budgetFor, enemies,
                canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain);
        }

        /// <summary>Zero-length paths for the living models - always move-through-valid.</summary>
        public static List<ModelMoveEntry> HoldExactPositions(List<DataBinding<ModelData>> living)
            => living.Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position })).ToList();

        /// <summary>
        /// Living enemy model footprints tagged with a per-unit key (so the validator can tell which
        /// models share an enemy unit); Aircraft marked uncontactable (#029); reserve-parked models at
        /// (0,0) excluded by convention.
        /// </summary>
        public static List<EnemyModelFootprint> LiveEnemyFootprints(ITableState tableState, PlayerID playerID)
        {
            var footprints = new List<EnemyModelFootprint>();
            int unitKey = 0;
            foreach (var unit in tableState.Units.Objects)
            {
                if (unit.PlayerID == playerID) continue;
                bool uncontactable = Rules.Dispatch.AircraftRules.IsAircraft(unit);
                bool anyLiving = false;
                foreach (var model in unit.Models)
                {
                    if (model is ModelData md && md.GetIsAlive() && (md.Position.x != 0f || md.Position.z != 0f))
                    {
                        footprints.Add(new EnemyModelFootprint(md.Position, md.BaseRadiusInches, unitKey,
                            uncontactable, md.BaseShape, md.Facing));
                        anyLiving = true;
                    }
                }
                if (anyLiving) unitKey++;
            }
            return footprints;
        }

        /// <summary>
        /// Packs the living models into a thin rank along the axis (<paramref name="axisDx"/>,
        /// <paramref name="axisDz"/>) centred on (<paramref name="centerX"/>, <paramref name="centerZ"/>) -
        /// the M8 barrier shape. Models sit edge-to-edge (0.1" base-to-base, cohesion-safe); when a single
        /// rank would stretch past the 9" all-models coherency limit the line wraps into parallel ranks,
        /// so the result is always cohesion-valid. Nearest-model-to-slot assignment keeps per-model moves
        /// small (the same idea as PackGrid's cell assignment).
        /// </summary>
        public static List<ModelMoveEntry> PackLine(IReadOnlyList<DataBinding<ModelData>> models,
            float centerX, float centerZ, float axisDx, float axisDz)
        {
            if (models.Count == 0) return new List<ModelMoveEntry>();
            float axisLen = MathF.Sqrt(axisDx * axisDx + axisDz * axisDz);
            if (axisLen < 1e-6f) { axisDx = 1f; axisDz = 0f; axisLen = 1f; }
            float ax = axisDx / axisLen, az = axisDz / axisLen;

            const float gap = 0.1f;
            // Uniform spacing from the largest footprint (mirrors GridSpacingXZ's worst-case approach).
            float maxHalf = 0f, maxHalfPerp = 0f;
            foreach (var mb in models)
            {
                var m = mb.GetValue();
                var (hx, hz) = BaseShapeGeometry.FootprintHalfExtents(m.BaseShape, m.Facing);
                maxHalf = MathF.Max(maxHalf, MathF.Max(hx, hz));
                maxHalfPerp = MathF.Max(maxHalfPerp, MathF.Min(hx, hz));
            }
            float spacing = 2f * maxHalf + gap;

            // Cap rank length under the 9" all-models rule (with margin); wrap the excess into
            // parallel ranks stacked along the perpendicular.
            const float maxRankSpanInches = 8.5f;
            int perRank = Math.Max(2, (int)(maxRankSpanInches / spacing) + 1);
            int rankCount = (int)MathF.Ceiling(models.Count / (float)perRank);
            int columns = (int)MathF.Ceiling(models.Count / (float)rankCount);
            float perpSpacing = 2f * maxHalf + gap;

            // Slot positions, rank-major, centred on the requested point.
            var slots = new List<Position>(models.Count);
            for (int i = 0; i < models.Count; i++)
            {
                int rank = i / columns;
                int col = i % columns;
                int inThisRank = Math.Min(columns, models.Count - rank * columns);
                float along = (col - (inThisRank - 1) / 2f) * spacing;
                float across = (rank - (rankCount - 1) / 2f) * perpSpacing;
                slots.Add(new Position(
                    centerX + ax * along - az * across,
                    centerZ + az * along + ax * across));
            }

            // Greedy nearest-model-to-slot assignment.
            var entries = new List<ModelMoveEntry>(models.Count);
            var used = new bool[models.Count];
            foreach (Position slot in slots)
            {
                int best = -1;
                float bestDist = float.MaxValue;
                for (int m = 0; m < models.Count; m++)
                {
                    if (used[m]) continue;
                    var p = models[m].GetValue().Position;
                    float d = (p.x - slot.x) * (p.x - slot.x) + (p.z - slot.z) * (p.z - slot.z);
                    if (d < bestDist) { bestDist = d; best = m; }
                }
                used[best] = true;
                entries.Add(new ModelMoveEntry(models[best], new List<Position> { slot }));
            }
            return entries;
        }

    }
}
