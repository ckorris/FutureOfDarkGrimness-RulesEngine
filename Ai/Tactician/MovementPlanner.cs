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
    /// (plan D1) by its resolver tests plus the 200-game benchmark outcome hashes recorded in the
    /// #191 ledger. #256 deliberately moved that baseline (measure-and-correct budgets, the S2
    /// re-aim, the S4 snake); #264 issue 6 moved it again (the solo skirt capped at +/-60 degrees,
    /// was +/-100 - past perpendicular a skirt is a retreat, and it was taken at the full rush
    /// budget). The re-pinned 2026-07-23 hashes are F82D5A91B0119955 (builtin mirror, 27/27/146)
    /// / A7EEB33FD9CEFC6A (builtin vs builtin-basic, 36/25/139), zero faults, zero timeouts,
    /// reproducible across duplicate runs at DOP 16. The previous pins were 3674C906996F34CC /
    /// CE3DC8150005FF2C - every hash reference older than this note refers to those.
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
            Func<float, float, List<ModelMoveEntry>>? reaimAt = null,
            Func<float, List<ModelMoveEntry>>? snakeAt = null)
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
                bool impassiblePresent = errors.Count > 0
                    && errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain);
                bool friendlyPresent = errors.Count > 0
                    && errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedOnFriendlyUnit);

                // #256 S4 + #264 issue 5: the on-path snake goes FIRST and fires whenever ANY impassible
                // fault is PRESENT (not only when it is the SOLE fault). The impassible fault is the
                // structural one - the formation is wider than the corridor the route threads (the walled
                // pocket's grid pack needed 0.19" of a 12" budget) - and the snake, whose files ride the
                // route clear-by-construction, is the tool that fixes it. Round-1 density mixes it with an
                // EndedOnFriendly fault (a friendly parked in the pocket); the old errors.All gate then
                // stayed shut and the ladder halved to a sub-inch shuffle. The snake re-validates below, so
                // a co-occurring friendly fault only means it may find no valid arc, never that it returns
                // an illegal move.
                if (snakeAt != null && step >= MinBackoffStepInches && living.Count > 0
                    && impassiblePresent)
                {
                    List<ModelMoveEntry> snake = snakeAt(step);
                    // Gate: the head must really thread the corridor (not a degenerate stay), and the
                    // unit must gain SOME ground - the first snake move stretches more than it advances,
                    // so the S2-style half-forward gate would wrongly reject it.
                    if (MaxModelMove(snake) >= 0.5f * step
                        && ForwardProgress(living, candidate, snake) > MinBackoffStepInches
                        && Validate(snake, out _))
                        return snake;
                }

                // #256 S2 + #264 issue 5: side-step the pack anchor to clear a friendly parked on the
                // arrival spot rather than halve toward zero. Fires when friendly-stacking is the SOLE
                // fault (the original #256 WayTooManyInBack case - all callers, INCLUDING the solo bot),
                // or - only when a snake exists to have taken the impassible fault first, i.e. the
                // Tactician - on the friendly RESIDUE of a mixed fault the snake could not thread. The
                // snakeAt guard on that second arm keeps the solo bot's re-aim gate exactly
                // FriendlyStackingIsSoleObstacle, so its pinned D1 baseline does not move. TryLateralReaim
                // re-validates every offset, so widening when it fires cannot submit an illegal move.
                if (reaimAt != null && step >= MinBackoffStepInches && living.Count > 0
                    && (FriendlyStackingIsSoleObstacle(errors)
                        || (snakeAt != null && friendlyPresent && impassiblePresent)))
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

        /// <summary>
        /// How far <paramref name="candidate"/> moves the unit's centroid along the direction the
        /// BLOCKED candidate was trying to go (#256 S4's gate; 0 when the blocked candidate itself
        /// went nowhere - nothing to make progress toward).
        /// </summary>
        private static float ForwardProgress(List<DataBinding<ModelData>> living,
            List<ModelMoveEntry> blockedCandidate, List<ModelMoveEntry> candidate)
        {
            float sx = living.Average(mb => mb.GetValue().Position.x);
            float sz = living.Average(mb => mb.GetValue().Position.z);
            (float bx, float bz) = EndCentroid(blockedCandidate);
            float fx = bx - sx, fz = bz - sz;
            float length = MathF.Sqrt(fx * fx + fz * fz);
            if (length < MinBackoffStepInches) return 0f;
            (float ex, float ez) = EndCentroid(candidate);
            return ((ex - sx) * fx + (ez - sz) * fz) / length;
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
        /// The G3-safe stand-still: <see cref="StayInPlace"/>'s reform validated like any ladder result,
        /// degrading to <see cref="HoldExactPositions"/> when even the re-pack is illegal - its slots can
        /// land on an adjacent friendly (#205's end-overlap rule). Callers that answer a movement request
        /// with a stay WITHOUT running the ladder must use this, not raw StayInPlace: the bench's seed-1051
        /// fault was the solo resolver's every-enemy-dead early-out submitting an unvalidated reform whose
        /// slot overlapped a teammate, faulting DefinePathStage.
        /// </summary>
        public static List<ModelMoveEntry> StayInPlaceValidated(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, Func<ModelMoveEntry, ModelMoveBudget> budgetFor,
            List<EnemyModelFootprint> enemies, bool canMoveThroughEnemies, bool ignoresDifficultTerrain,
            bool ignoresImpassibleTerrain, List<ITerrain> terrain,
            IReadOnlyList<EnemyModelFootprint>? friendlies)
        {
            List<ModelMoveEntry> stay = StayInPlace(unit);
            bool valid = MovementUtilities.ValidatePaths(stay, budgetFor, enemies, canMoveThroughEnemies,
                ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain, out _, friendlies,
                lenientCoherency: true);
            return valid ? stay : HoldExactPositions(living);
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

        /// <summary>
        /// The #256 S4 corridor shape: every model's destination lies ON the pathfound polyline,
        /// staggered back from the head by base-width spacing - a snake that is clear-by-construction
        /// wherever the route itself is clear for one base. The endpoint packers (grid and straight
        /// line) place flank slots geometrically, so in a wall corridor they embed slots in terrain
        /// and every arc fails "moves through impassible" (the walled Battle Brothers pocket: the
        /// grid pack needed a 0.19" arc where the snake cleared at 3"). Units too long for one file
        /// under the 9" all-models rule wrap into parallel files (11 models: 2 files, ~1.75" wide vs
        /// the ~4.5" grid). The first snake move mostly STRETCHES the unit into the file (small
        /// centroid gain); from the next activation it flows down the route at ~full arc.
        /// </summary>
        public static List<ModelMoveEntry> BuildSnakeCandidate(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, List<Position> path, float arcLengthInches,
            IReadOnlyList<ITerrain> terrain, float baseRadiusInches, float maxDistanceInches)
        {
            if (arcLengthInches <= 0f || path.Count < 2 || living.Count == 0) return StayInPlace(unit);

            float spacing = 2f * baseRadiusInches + 0.1f; // cohesion-safe 0.1" gap, as PackLine
            // Wrap into parallel files when a single file would stretch past the 9" all-models rule.
            const float maxFileSpanInches = 8.5f;
            int ranksInOneFile = living.Count;
            int files = Math.Max(1, (int)MathF.Ceiling((ranksInOneFile - 1) * spacing / maxFileSpanInches));
            int ranks = (int)MathF.Ceiling(living.Count / (float)files);

            // The model nearest the first bend leads; stable order keeps the pairing deterministic.
            Position bend = path[Math.Min(1, path.Count - 1)];
            List<DataBinding<ModelData>> order = living
                .OrderBy(mb =>
                {
                    Position p = mb.GetValue().Position;
                    return (p.x - bend.x) * (p.x - bend.x) + (p.z - bend.z) * (p.z - bend.z);
                })
                .ToList();

            // #264 issue 4: the extra files used to STRADDLE the route (offsets -w/2 and +w/2). A
            // pathfound route only guarantees one base-width of clearance, so at a wall corner - where
            // the route grazes the piece by design - the inboard file lands INSIDE the wall and every
            // arc faults, snake included (measured: 11 models at a corner faulted at 12", 6" and 3",
            // validating only at a 1.06" shuffle of a 12" rush). Keep the lead file ON the route,
            // which is clear by construction, and fan the rest to whichever side is actually open.
            List<ModelMoveEntry>? fallback = null;
            foreach (float side in new[] { 1f, -1f })
            {
                List<ModelMoveEntry> candidate = BuildSnakeToSide(unit, living, order, path,
                    arcLengthInches, terrain, baseRadiusInches, maxDistanceInches, spacing, files, side);
                if (files == 1 || !ClipsImpassible(candidate, terrain, baseRadiusInches)) return candidate;
                fallback ??= candidate;
            }
            return fallback ?? StayInPlace(unit);
        }

        // One side-choice of the snake: file 0 rides the route, file k sits k spacings to `side`.
        private static List<ModelMoveEntry> BuildSnakeToSide(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, List<DataBinding<ModelData>> order,
            List<Position> path, float arcLengthInches, IReadOnlyList<ITerrain> terrain,
            float baseRadiusInches, float maxDistanceInches, float spacing, int files, float side)
        {
            float arc = arcLengthInches;
            for (int attempt = 0; attempt < RepackCorrectionAttempts; attempt++)
            {
                var candidate = new List<ModelMoveEntry>(order.Count);
                for (int i = 0; i < order.Count; i++)
                {
                    int rank = i / files;
                    int file = i % files;
                    float arcI = Math.Max(0.05f, arc - rank * spacing);
                    (Position endI, List<Position> passedI, _) =
                        GridPathfinder.AdvanceAlongPath(path, arcI, terrain, baseRadiusInches);

                    if (files > 1)
                    {
                        // Offset parallel files perpendicular to the LOCAL path direction at this point.
                        Position prev = passedI.Count > 0 ? passedI[^1] : path[0];
                        float dx = endI.x - prev.x, dz = endI.z - prev.z;
                        float len = MathF.Sqrt(dx * dx + dz * dz);
                        (dx, dz) = len < 1e-6f ? (1f, 0f) : (dx / len, dz / len);
                        float across = file * spacing * side;
                        endI = new Position(endI.x + across * -dz, endI.z + across * dx);
                    }

                    // #264 issue 4: join the route where this model actually stands, rather than
                    // hopping to its first bend - for a rank drawn up across the route's mouth that
                    // hop grazes the very piece the route detours around.
                    candidate.Add(new ModelMoveEntry(order[i],
                        RouteLegsFor(order[i].GetValue().Position, path, passedI,
                            new List<Position> { endI }, terrain, baseRadiusInches)));
                }

                float overshoot = MaxModelMove(candidate) - maxDistanceInches;
                if (overshoot <= 0f) return candidate;
                arc -= overshoot + RepackCorrectionSlackInches;
                if (arc <= 0f) break;
            }
            return StayInPlace(unit);
        }

        /// <summary>Whether any leg of the candidate sweeps through impassible terrain - the fault the
        /// snake exists to avoid, cheap to check here without the full validator (#264 issue 4).</summary>
        private static bool ClipsImpassible(List<ModelMoveEntry> candidate,
            IReadOnlyList<ITerrain> terrain, float baseRadiusInches)
        {
            foreach (ModelMoveEntry entry in candidate)
            {
                Position previous = entry.Model.GetValue().Position;
                foreach (Position leg in entry.Positions)
                {
                    if (!GridPathfinder.SegmentClear(terrain, previous, leg, baseRadiusInches)) return true;
                    previous = leg;
                }
            }
            return false;
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
                    RouteLegsFor(entry.Model.GetValue().Position, path, passed,
                        entry.Positions, terrain, baseRadiusInches)))
                .ToList();
        }

        /// <summary>
        /// The legs ONE model walks to its destination: onto the route at the point nearest it, then
        /// the route's waypoints ahead of that join, string-pulled so a model that can reach a later
        /// waypoint directly does not walk the dogleg.
        /// <para>
        /// #264 issue 4: every model used to be prefixed with the SAME traversed-waypoint list, which
        /// means jumping straight to the route's first bend. For a rank drawn up across the route's
        /// mouth that hop is both long and, at a wall corner, ILLEGAL - the far-flank model's line to
        /// the bend grazes the piece the route went around. So the pack was invalid at every arc
        /// until halving shrank it to nothing, and the #256 S4 snake inherited the same bad hop and
        /// could not rescue it (measured: pack and snake both faulted at 12", 6" and 3"; the snake
        /// only validated at a 1.06" shuffle). Joining at the nearest point keeps every leg on or
        /// alongside the route, where it is clear by construction.
        /// </para>
        /// </summary>
        private static List<Position> RouteLegsFor(Position from, List<Position> path,
            List<Position> passed, List<Position> tail, IReadOnlyList<ITerrain> terrain,
            float baseRadiusInches)
        {
            if (tail.Count == 0) return new List<Position>(passed);

            (Position join, float joinArc) = NearestPointOnRoute(path, from);
            var legs = new List<Position> { from, join };

            float arc = 0f;
            for (int i = 1; i <= passed.Count && i < path.Count; i++)
            {
                arc += Dist(path[i - 1], path[i]);
                if (arc > joinArc + 1e-3f) legs.Add(path[i]);
            }
            legs.AddRange(tail);

            List<Position> pulled = GridPathfinder.StringPull(terrain, legs, baseRadiusInches);
            pulled.RemoveAt(0); // the anchor is the model's own position, not a leg
            return pulled;
        }

        /// <summary>The point on the route closest to <paramref name="from"/>, with its arc position
        /// along the route (#264 issue 4: where a model off the route should join it).</summary>
        private static (Position Point, float Arc) NearestPointOnRoute(List<Position> path, Position from)
        {
            Position best = path[0];
            float bestArc = 0f;
            float bestDistanceSq = float.PositiveInfinity;
            float arcSoFar = 0f;

            for (int i = 1; i < path.Count; i++)
            {
                Position a = path[i - 1], b = path[i];
                float abx = b.x - a.x, abz = b.z - a.z;
                float lengthSq = abx * abx + abz * abz;
                float t = lengthSq <= 1e-8f
                    ? 0f
                    : Math.Clamp(((from.x - a.x) * abx + (from.z - a.z) * abz) / lengthSq, 0f, 1f);
                var point = new Position(a.x + t * abx, a.z + t * abz);
                float dx = point.x - from.x, dz = point.z - from.z;
                float distanceSq = dx * dx + dz * dz;
                float segment = MathF.Sqrt(lengthSq);
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    best = point;
                    bestArc = arcSoFar + t * segment;
                }
                arcSoFar += segment;
            }
            return (best, bestArc);
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
            => PlanMoveAlongRoute(unit, living, tableState, goal, moveBudgetInches, maxDistanceInches,
                budgetFor, canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain,
                formation, lineAxis, sharedGrid).Move;

        /// <summary>
        /// <see cref="PlanMoveToward"/>, additionally handing back the ROUTE it followed (the
        /// start -> goal polyline, detouring around impassible terrain). Callers that grade how much
        /// progress a candidate made need route distance, not straight-line distance: rounding a
        /// large wall closes ~zero straight-line gap and its first leg can point away from the goal
        /// (#264 issue 1). The route is already computed here, so this costs the caller nothing.
        /// </summary>
        public static (List<ModelMoveEntry> Move, List<Position> Route) PlanMoveAlongRoute(
            DataBinding<UnitData> unit,
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
                TerrainGrid grid = sharedGrid?.Invoke()
                    ?? TerrainGridCache.Get(tableState, terrain, baseRadius, ignoresDifficultTerrain);
                path = GridPathfinder.FindPath(grid, terrain, start, goal, baseRadius);
                // #264 issue 3: no route to the goal is not a reason to walk INTO the wall. Head for
                // the reachable point closest to it instead - the straight line below remains only
                // for the case where standing still is genuinely as close as the unit can get.
                path ??= GridPathfinder.FindPathToNearestReachable(grid, terrain, start, goal, baseRadius);
            }
            path ??= new List<Position> { start, goal };

            float budget = moveBudgetInches;
            (_, _, bool crossesDifficult) =
                GridPathfinder.AdvanceAlongPath(path, budget, terrain, baseRadius);
            if (crossesDifficult && !ignoresDifficultTerrain)
                budget = Math.Min(budget, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES - 0.001f);

            List<ModelMoveEntry> move = ValidateWithBackoff(
                arc => BuildPathCandidate(unit, living, path, arc, terrain, baseRadius,
                    maxDistanceInches, formation, lineAxis),
                budget, unit, living, budgetFor, enemies,
                canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain, friendlies,
                // #256 S2: a friendly parked on the arrival spot side-steps the endpoint fan-out
                // instead of halving the arc toward a stall (the Warriors-toward-(7,30) row).
                (arc, lat) => BuildPathCandidate(unit, living, path, arc, terrain, baseRadius,
                    maxDistanceInches, formation, lineAxis, lat),
                // #256 S4: a formation too wide for the route's corridor falls back to the on-path
                // snake instead of halving to a crawl (the walled Battle Brothers pocket).
                arc => BuildSnakeCandidate(unit, living, path, arc, terrain, baseRadius,
                    maxDistanceInches));
            return (move, path);
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
