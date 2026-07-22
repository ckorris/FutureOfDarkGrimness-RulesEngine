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

        // #256 S2 re-aim: when a centered re-pack's ONLY fault is ending stacked on a friendly, side-step
        // the pack anchor perpendicular to the move (in these multiples of a base width) before conceding
        // distance to the halving ladder. Nearest offset first, alternating sides - deterministic order.
        // Reaches 4 base widths (clearing a blocker takes ~pack-half-width + blocker-half-width; an
        // 11-model pack is already ~1.7 widths from center to edge), in HALF-width steps past 2: the
        // WayTooManyInBack reconstruction probe showed the clearing window can be under half a width wide
        // (2.2 collided, 2.8 cleared, 3.0+ blew the budget), so whole-width jumps stride right over it.
        private static readonly float[] ReaimLateralWidthMultiples =
            { 1f, -1f, 2f, -2f, 2.5f, -2.5f, 3f, -3f, 3.5f, -3.5f, 4f, -4f };

        // #256 measure-and-correct bounds: pack-build attempts per candidate, and the safety margin
        // taken off each correction (the step<->per-model-move response is ~1:1, but slot
        // reassignment can wobble it slightly between attempts). 8 attempts, not 4: with an S2
        // lateral offset in play the response flattens (the offset's cost doesn't shrink with the
        // step) and pairing flips can bump the measure back up mid-descent - 4 attempts gave up on
        // feasible side-steps and degraded them to stays (probed, WayTooManyInBack reconstruction).
        public const int RepackCorrectionAttempts = 8;
        public const float RepackCorrectionSlackInches = 0.01f;

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
        /// rule), the step corrected so each model's actual move stays within
        /// <paramref name="maxDistanceInches"/> (#256). A non-positive step degrades to
        /// <see cref="StayInPlace"/> (which is why the unit binding is a parameter - that fallback
        /// includes dead models' zero-length paths when 0-1 live).
        /// <para>
        /// <paramref name="lateralOffsetInches"/> (#256 S2) shifts the pack anchor perpendicular to the
        /// move direction - the side-step the ladder tries when a centered re-pack would end stacked on a
        /// friendly. It adds to each model's travel, so the measure-and-correct loop below shrinks the
        /// forward step to keep the worst per-model move within budget (a side-step trades advance for
        /// clearance, never exceeding the allowance).
        /// </para>
        /// </summary>
        public static List<ModelMoveEntry> BuildCandidate(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living,
            float cx, float cz, float ndx, float ndz, float step, float maxDistanceInches,
            EFormation formation = EFormation.Grid, float lateralOffsetInches = 0f)
        {
            if (step <= 0f) return StayInPlace(unit);

            // The perpendicular to the (unit-length) move direction; a positive offset side-steps one way.
            float ax = cx + lateralOffsetInches * -ndz;
            float az = cz + lateralOffsetInches * ndx;

            if (living.Count == 1)
            {
                var only = living[0].GetValue();
                var dest = new Position(
                    only.Position.x + ndx * step + lateralOffsetInches * -ndz,
                    only.Position.z + ndz * step + lateralOffsetInches * ndx);
                return new List<ModelMoveEntry> { new ModelMoveEntry(living[0], new List<Position> { dest }) };
            }

            // #256 measure-and-correct (replaces the worst-case ClampRepackStep pre-clamp, which
            // subtracted spread + grid radius from the budget and zeroed big units' moves entirely -
            // an 11-model combined unit could not advance at all): pack at the desired step, measure
            // the actual worst per-model move, and shrink the step by the overshoot. PackGrid's
            // nearest-model-to-slot assignment makes a translation cost ~step per model, so a few
            // passes converge; any residue is caught by the ladder's ValidatePaths.
            for (int attempt = 0; attempt < RepackCorrectionAttempts; attempt++)
            {
                float destX = ax + ndx * step;
                float destZ = az + ndz * step;
                List<ModelMoveEntry> candidate = formation == EFormation.Line
                    // A barrier line runs PERPENDICULAR to the move direction (across the lane being walled).
                    ? PackLine(living, destX, destZ, -ndz, ndx)
                    : CohesiveFormation.PackGrid(living, destX, destZ);
                candidate = ImprovePairing(candidate);
                float overshoot = MaxModelMove(candidate) - maxDistanceInches;
                if (overshoot <= 0f) return candidate;
                step -= overshoot + RepackCorrectionSlackInches;
                if (step <= 0f) break;
            }
            // Even a near-zero-step re-pack exceeds the budget (casualty holes wider than the move
            // allowance): stay put and let the ladder's own fallbacks take over.
            return StayInPlace(unit);
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
        /// uses (#159: lenientCoherency, matching DefinePathStage - a unit already broken by a casualty may
        /// hold rather than be rejected for a coherency it can't restore), halving the step until it passes;
        /// then reform-in-place; then hold exact positions (zero-length paths can't move through anything, so
        /// the last resort is always valid). The returned move is one the engine will accept.
        /// </summary>
        public static List<ModelMoveEntry> ValidateWithBackoff(
            Func<float, List<ModelMoveEntry>> candidateAt, float initialStep,
            DataBinding<UnitData> unit, List<DataBinding<ModelData>> living,
            Func<ModelMoveEntry, ModelMoveBudget> budgetFor, List<EnemyModelFootprint> enemies,
            bool canMoveThroughEnemies, bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            List<ITerrain> terrain, IReadOnlyList<EnemyModelFootprint>? friendlies = null,
            Func<float, float, List<ModelMoveEntry>>? reaimAt = null)
        {
            bool Validate(List<ModelMoveEntry> c, out List<ReasonForInvalidMove> errs) =>
                MovementUtilities.ValidatePaths(c, budgetFor, enemies, canMoveThroughEnemies,
                    ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain, out errs, friendlies,
                    lenientCoherency: true);

            float step = initialStep;
            List<ModelMoveEntry> candidate = candidateAt(step);
            bool valid = Validate(candidate, out List<ReasonForInvalidMove> errors);

            int attempts = 0;
            while (!valid && attempts < MaxBackoffAttempts)
            {
                // #256 S2: when the re-packed formation's ONLY fault is ending stacked on a friendly,
                // halving just crawls toward zero (the friendly is still in the way - the WayTooManyInBack
                // corner pocket). Side-step the pack anchor a few base-widths each way at the SAME step
                // first; if any offset clears, keep the full advance instead of surrendering it.
                if (reaimAt != null && step >= MinBackoffStepInches && living.Count > 0
                    && FriendlyStackingIsSoleObstacle(errors))
                {
                    List<ModelMoveEntry>? reaimed = TryLateralReaim(reaimAt, step, living, candidate,
                        c => Validate(c, out _));
                    if (reaimed != null) return reaimed;
                }

                step *= 0.5f;
                candidate = step < MinBackoffStepInches
                    ? StayInPlace(unit)
                    : candidateAt(step);
                valid = Validate(candidate, out errors);
                attempts++;
            }

            if (!valid)
            {
                // Reform in place to close any casualty gaps...
                candidate = StayInPlace(unit);
                valid = Validate(candidate, out _);

                // ...but if even that is rejected (a unit intermingled with enemies can't re-pack without
                // a model crossing an enemy base), hold exact positions.
                if (!valid)
                    candidate = HoldExactPositions(living);
            }

            return candidate;
        }

        /// <summary>
        /// True when the move is rejected ONLY because one or more models would end stacked on a friendly
        /// (#256 S2) - the case a lateral side-step of the pack can clear. A mix that also carries a
        /// terrain, enemy, budget, or coherency fault is left to the halving ladder (a side-step can't fix
        /// those and might worsen them).
        /// </summary>
        private static bool FriendlyStackingIsSoleObstacle(List<ReasonForInvalidMove> errors)
            => errors.Count > 0 && errors.All(e => e.ErrorReasonType == EErrorReasonType.EndedOnFriendlyUnit);

        /// <summary>
        /// Probe a few lateral offsets of the pack anchor, returning the first that validates AND keeps
        /// at least half the blocked candidate's forward progress (nearest offset first, alternating
        /// sides), or null if none clear. Each probe shortens the forward step to sqrt(step^2 - lat^2) -
        /// the side-step trades forward advance for clearance INSIDE the same budget circle. Probing the
        /// full step with the offset stacked on top instead makes the builder's measure-and-correct loop
        /// absorb the whole lateral cost, and past ~3 base widths it fails to converge and degrades to a
        /// valid-but-useless stay (probed on the WayTooManyInBack reconstruction: an 11-model advance
        /// collapsed to 0.1"). The forward-progress gate rejects any such degenerate candidate - the
        /// halving ladder is the better fallback then.
        /// </summary>
        private static List<ModelMoveEntry>? TryLateralReaim(
            Func<float, float, List<ModelMoveEntry>> reaimAt, float step,
            List<DataBinding<ModelData>> living, List<ModelMoveEntry> blockedCandidate,
            Func<List<ModelMoveEntry>, bool> isValid)
        {
            // Forward = the blocked candidate's own centroid displacement (the direction the caller is
            // actually trying to go, whatever the candidate shape). A near-zero displacement means the
            // blocked candidate itself was already a stay - nothing worth re-aiming.
            float sx = living.Average(mb => mb.GetValue().Position.x);
            float sz = living.Average(mb => mb.GetValue().Position.z);
            (float bx, float bz) = EndCentroid(blockedCandidate);
            float fx = bx - sx, fz = bz - sz;
            float forwardLength = MathF.Sqrt(fx * fx + fz * fz);
            if (forwardLength < MinBackoffStepInches) return null;
            (fx, fz) = (fx / forwardLength, fz / forwardLength);

            float baseWidth = 2f * living.Max(mb => mb.GetValue().BaseRadiusInches);
            foreach (float multiple in ReaimLateralWidthMultiples)
            {
                float lat = multiple * baseWidth;
                if (Math.Abs(lat) >= step) continue; // no forward room left inside the budget circle
                float forwardStep = MathF.Sqrt(step * step - lat * lat);
                List<ModelMoveEntry> candidate = reaimAt(forwardStep, lat);
                (float ex, float ez) = EndCentroid(candidate);
                float forwardProgress = (ex - sx) * fx + (ez - sz) * fz;
                if (forwardProgress >= 0.5f * forwardLength && isValid(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>Centroid of a candidate's end positions (models with empty paths stay put).</summary>
        private static (float X, float Z) EndCentroid(List<ModelMoveEntry> moves)
        {
            float x = 0f, z = 0f;
            int count = 0;
            foreach (ModelMoveEntry move in moves)
            {
                Position end = move.Positions.Count > 0
                    ? move.Positions[^1]
                    : move.Model.GetValue().Position;
                x += end.x; z += end.z; count++;
            }
            return count == 0 ? (0f, 0f) : (x / count, z / count);
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
            // #256: without the pairing cleanup a stay can scramble models across rows when the
            // current and canonical row counts mismatch (measured 2.9" of churn on a parked grid).
            return ImprovePairing(CohesiveFormation.PackGrid(living, cx, cz));
        }

        /// <summary>
        /// A path-following candidate (#191 A3b): the unit travels <paramref name="arcLengthInches"/>
        /// along <paramref name="path"/>, every model sharing the path's interior waypoints (so the
        /// whole unit funnels through corridors) and fanning out into the formation at the endpoint.
        /// The arc length is the ladder's backoff knob, exactly like the straight candidate's step.
        /// <paramref name="lateralOffsetInches"/> (#256 S2) shifts the formation anchor perpendicular
        /// to the path's final segment - the endpoint fan-out side-steps a friendly parked on the
        /// arrival spot while the funnelled waypoints stay on the route.
        /// </summary>
        public static List<ModelMoveEntry> BuildPathCandidate(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, List<Position> path, float arcLengthInches,
            IReadOnlyList<ITerrain> terrain, float baseRadiusInches, float maxDistanceInches,
            EFormation formation = EFormation.Grid, (float X, float Z)? lineAxis = null,
            float lateralOffsetInches = 0f)
        {
            if (arcLengthInches <= 0f || path.Count < 2) return StayInPlace(unit);

            // #256 measure-and-correct, same scheme as BuildCandidate (the worst-case pre-clamp here
            // cost wide combined units their whole budget). The endpoint - and with it the formation
            // anchor - moves with the arc, so the pack is rebuilt per attempt.
            float arc = arcLengthInches;
            for (int attempt = 0; attempt < RepackCorrectionAttempts; attempt++)
            {
                List<ModelMoveEntry> candidate = PathCandidateAt(unit, living, path, arc, terrain,
                    baseRadiusInches, formation, lineAxis, lateralOffsetInches);
                float overshoot = MaxModelMove(candidate) - maxDistanceInches;
                if (overshoot <= 0f) return candidate;
                arc -= overshoot + RepackCorrectionSlackInches;
                if (arc <= 0f) break;
            }
            return StayInPlace(unit);
        }

        /// <summary>One un-clamped path-following pack at a given arc length (#256's measure loop).</summary>
        private static List<ModelMoveEntry> PathCandidateAt(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, List<Position> path, float arcLengthInches,
            IReadOnlyList<ITerrain> terrain, float baseRadiusInches,
            EFormation formation, (float X, float Z)? lineAxis, float lateralOffsetInches = 0f)
        {
            (Position endpoint, List<Position> passed, _) =
                GridPathfinder.AdvanceAlongPath(path, arcLengthInches, terrain, baseRadiusInches);

            Position previous = passed.Count > 0 ? passed[^1] : path[0];
            float dirX = endpoint.x - previous.x;
            float dirZ = endpoint.z - previous.z;
            float length = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
            (dirX, dirZ) = length < 1e-6f ? (1f, 0f) : (dirX / length, dirZ / length);

            // #256 S2: the re-aim shifts the arrival anchor perpendicular to the final segment.
            endpoint = new Position(
                endpoint.x + lateralOffsetInches * -dirZ,
                endpoint.z + lateralOffsetInches * dirX);

            List<ModelMoveEntry> destinations;
            if (living.Count == 1)
            {
                destinations = new List<ModelMoveEntry>
                    { new ModelMoveEntry(living[0], new List<Position> { endpoint }) };
            }
            else
            {
                // A Line spreads along the caller's axis when given (M8: perpendicular to the lane
                // being walled, regardless of approach direction); default: across the move.
                (float lineX, float lineZ) = lineAxis ?? (-dirZ, dirX);
                destinations = formation == EFormation.Line
                    ? PackLine(living, endpoint.x, endpoint.z, lineX, lineZ)
                    : CohesiveFormation.PackGrid(living, endpoint.x, endpoint.z);
                destinations = ImprovePairing(destinations);
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
            EFormation formation = EFormation.Grid, (float X, float Z)? lineAxis = null,
            Func<TerrainGrid>? sharedGrid = null)
        {
            var terrain = tableState.Terrain.Objects.ToList();
            var enemies = LiveEnemyFootprints(tableState, unit.GetValue().PlayerID);
            var friendlies = LiveFriendlyFootprints(tableState, unit.GetValue().PlayerID, unit.GetValue().ID);
            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);
            var start = new Position(cx, cz);
            float baseRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);

            // Grid construction is the expensive part (thousands of point tests), so: straight-clear
            // paths never touch it, and callers planning MANY candidates in one activation share one
            // build via sharedGrid (the A4-2 hot path - per-candidate builds cost ~0.5s per decision).
            List<Position>? path = null;
            if (!ignoresImpassibleTerrain
                && terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Impassible)
                    && t.Shape.DoesPathIntersectZone(new Float2(start.x, start.z),
                        new Float2(goal.x, goal.z), baseRadius)))
            {
                TerrainGrid grid = sharedGrid?.Invoke() ?? TerrainGrid.Build(terrain, baseRadius);
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
                    maxDistanceInches, formation, lineAxis),
                budget, unit, living, budgetFor, enemies,
                canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain, friendlies,
                // #256 S2: a friendly parked on the arrival spot side-steps the endpoint fan-out
                // instead of halving the arc toward a stall (the Warriors-toward-(7,30) row).
                (arc, lat) => BuildPathCandidate(unit, living, path, arc, terrain, baseRadius,
                    maxDistanceInches, formation, lineAxis, lat));
        }

        /// <summary>
        /// Local-search cleanup of a pack's model-to-slot pairing (#256): repeatedly swap two
        /// models' slots while that lowers the pair's WORST move (the quantity the per-model
        /// budget caps), with the pair's summed distance as tie-break. The packers' greedy
        /// nearest-model-to-slot assignment is fine near zero translation but REVERSES rank order
        /// once the step exceeds the grid spacing (the front model grabs the rearmost slot), and
        /// any fixed rank-pairing trades that for cross-row leaps when the current and canonical
        /// row counts mismatch - bottleneck-2-opt fixes both (a pure translation relaxes to the
        /// identity pairing). The slot set - and with it cohesion and enemy gaps - is unchanged;
        /// fixed scan order + strict improvement + bounded passes keep it deterministic.
        /// </summary>
        private static List<ModelMoveEntry> ImprovePairing(List<ModelMoveEntry> packed)
        {
            if (packed.Count <= 1) return packed;
            Position[] starts = packed.Select(e => e.Model.GetValue().Position).ToArray();
            Position[] slots = packed.Select(e => e.Positions[^1]).ToArray();

            const float eps = 1e-4f;
            bool improved = true;
            for (int pass = 0; pass < 32 && improved; pass++)
            {
                improved = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    for (int j = i + 1; j < slots.Length; j++)
                    {
                        float di = Dist(starts[i], slots[i]), dj = Dist(starts[j], slots[j]);
                        float si = Dist(starts[i], slots[j]), sj = Dist(starts[j], slots[i]);
                        float keptMax = Math.Max(di, dj), swappedMax = Math.Max(si, sj);
                        if (swappedMax < keptMax - eps
                            || (swappedMax < keptMax + eps && si + sj + eps < di + dj))
                        {
                            (slots[i], slots[j]) = (slots[j], slots[i]);
                            improved = true;
                        }
                    }
                }
            }

            var entries = new List<ModelMoveEntry>(packed.Count);
            for (int i = 0; i < packed.Count; i++)
                entries.Add(new ModelMoveEntry(packed[i].Model, new List<Position> { slots[i] }));
            return entries;
        }

        private static float Dist(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Worst per-model total path length of a candidate (start -> waypoints -> slot), the
        /// quantity ValidateOutOfMoveRange caps - what the #256 correction loops measure against.
        /// </summary>
        private static float MaxModelMove(List<ModelMoveEntry> moves)
        {
            float max = 0f;
            foreach (ModelMoveEntry move in moves)
            {
                Position previous = move.Model.GetValue().Position;
                float total = 0f;
                foreach (Position p in move.Positions)
                {
                    float dx = p.x - previous.x, dz = p.z - previous.z;
                    total += MathF.Sqrt(dx * dx + dz * dz);
                    previous = p;
                }
                if (total > max) max = total;
            }
            return max;
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
        /// Living friendly model footprints (same team as <paramref name="playerID"/>, EXCLUDING the moving
        /// unit) - the #205 end-overlap obstacles the AI must not stack on. Team-based so it matches the
        /// authoritative <see cref="MovementUtilities.GetFriendlyModelFootprints"/> (in the 1v1 pool that is
        /// just "my other units"); reserve-parked models at (0,0) are excluded by convention, exactly like
        /// <see cref="LiveEnemyFootprints"/>. Reuses EnemyModelFootprint purely as a base-footprint carrier.
        /// </summary>
        public static List<EnemyModelFootprint> LiveFriendlyFootprints(ITableState tableState, PlayerID playerID,
            UnitID excludeUnitId)
        {
            ITeam? team = tableState.Teams.Objects.FirstOrDefault(t => t.Players.Contains(playerID));
            IReadOnlyList<PlayerID> allied = team != null ? team.Players : new List<PlayerID> { playerID };

            var footprints = new List<EnemyModelFootprint>();
            int unitKey = 0;
            foreach (var unit in tableState.Units.Objects)
            {
                if (!allied.Contains(unit.PlayerID)) continue;
                if (unit.ID.Equals(excludeUnitId)) continue;
                bool anyLiving = false;
                foreach (var model in unit.Models)
                {
                    if (model is ModelData md && md.GetIsAlive() && (md.Position.x != 0f || md.Position.z != 0f))
                    {
                        footprints.Add(new EnemyModelFootprint(md.Position, md.BaseRadiusInches, unitKey,
                            false, md.BaseShape, md.Facing));
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
