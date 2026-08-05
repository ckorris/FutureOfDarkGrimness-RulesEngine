using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// Computes the defender's reactive pile-in moves after a charge. GF v3.5.1 p.9:
    /// defender models not in base contact must move by up to 3" to get into base contact
    /// with a charging model, or as close as possible, maintaining unit coherency.
    /// <para>
    /// #330: "into base contact" is read as maximize-the-contact-count, the way a player would
    /// actually pile in. Contact slots are sampled around every charging model's base and
    /// defenders are greedily assigned to reachable free slots (most-constrained defender first),
    /// so a second-rank model slides around its front rank to an open flank instead of stopping
    /// dead against a friend's back. Defenders that can reach no slot fall back to the previous
    /// behavior: a straight step toward the nearest charger, clamped at the first obstruction.
    /// Models keep their facing throughout (pile-in has never rotated).
    /// </para>
    /// </summary>
    public static class PileInUtilities
    {
        public const float MAX_PILE_IN_DISTANCE_INCHES = 3f;

        // Anything closer than this base-to-base counts as already in BTB. Guards against float drift.
        private const float BTB_EPSILON_INCHES = 0.01f;

        // Don't bother applying a move shorter than this; reduces noise.
        private const float MIN_MEANINGFUL_STEP_INCHES = 0.01f;

        // #330 slot sampling: candidate contact positions per charging model. 36 = one per 10 degrees,
        // dense enough that a 25mm base never misses the gap between neighbors on any base we ship.
        private const int SLOT_DIRECTIONS_PER_CHARGER = 36;

        // A slot is settled at this base-to-base gap: positive so float drift can never read as overlap,
        // comfortably under BTB_EPSILON_INCHES so the result still counts as base contact everywhere.
        private const float SLOT_CONTACT_GAP_INCHES = 0.003f;

        // End-state overlap tolerance between any two bases. Contact (gap ~0) is legal; deeper is not.
        private const float OVERLAP_TOLERANCE_INCHES = 0.005f;

        // Swept-path tolerance: a path is blocked only if the first touch happens meaningfully before the
        // destination; grazing an obstacle at the final approach (every contact slot ends touching its
        // charger by definition) must not read as blocked.
        private const float PATH_TOUCH_TOLERANCE_INCHES = 0.02f;

        public readonly struct PileInMove
        {
            public readonly DataBinding<ModelData> Model;
            public readonly Position NewPosition;

            public PileInMove(DataBinding<ModelData> model, Position newPosition)
            {
                Model = model;
                NewPosition = newPosition;
            }
        }

        public static List<PileInMove> ComputePileInMoves(
            IReadOnlyList<DataBinding<ModelData>> chargingModels,
            IReadOnlyList<DataBinding<ModelData>> defendingModels,
            IEnumerable<ITerrain>? terrain,
            IReadOnlyList<EnemyModelFootprint>? otherEnemyModels = null)
        {
            // Enemy models the defender must not overlap while piling in, OTHER than the charging unit it is
            // moving toward (third-party enemy units, or a unit it is already engaged with). Without these a
            // defender piling toward its charger could plow straight through a different enemy's base (#159).
            IReadOnlyList<EnemyModelFootprint> otherEnemies =
                otherEnemyModels ?? System.Array.Empty<EnemyModelFootprint>();

            List<DataBinding<ModelData>> liveChargers = chargingModels
                .Where(m => m.GetValue().GetIsAlive()).ToList();
            List<DataBinding<ModelData>> liveDefenders = defendingModels
                .Where(m => m.GetValue().GetIsAlive()).ToList();

            if (liveChargers.Count == 0 || liveDefenders.Count == 0)
                return new List<PileInMove>();

            List<ITerrain> impassable = terrain?
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList() ?? new List<ITerrain>();

            // Working positions: defender -> current planned position. Updated as we accept moves so later
            // candidates account for earlier defenders' new spots.
            Dictionary<DataBinding<ModelData>, Position> workingDefenderPositions = liveDefenders
                .ToDictionary(d => d, d => d.GetValue().Position);

            // Candidate set: defenders not already in BTB with any charger, paired with their nearest charger
            // and current b2b. Ordered closest-to-front first so the leading models settle their final
            // positions before models behind them — later defenders' obstruction checks then see the
            // up-to-date layout.
            var candidates = new List<(DataBinding<ModelData> Defender, DataBinding<ModelData> Charger, float B2B)>();
            foreach (DataBinding<ModelData> d in liveDefenders)
            {
                float b2b = NearestB2BAt(d.GetValue().Position, d.GetValue().BaseShape, d.GetValue().Facing, liveChargers,
                    out DataBinding<ModelData>? nearest);
                if (nearest == null) continue;
                if (b2b <= BTB_EPSILON_INCHES) continue;
                candidates.Add((d, nearest, b2b));
            }
            candidates.Sort((a, b) => a.B2B.CompareTo(b.B2B));

            HashSet<DataBinding<ModelData>> movedDefenders = new HashSet<DataBinding<ModelData>>();

            // --- Phase 1 (#330): assign defenders to contact slots around the chargers. ---
            // Slots depend on the defender's base shape + facing (a 4"-wide rectangle sits at a different
            // spot than a 25mm circle), so slot rings are computed once per distinct (shape, facing) group.
            List<DataBinding<ModelData>> slotAssigned = AssignDefendersToContactSlots(
                candidates, liveChargers, liveDefenders, workingDefenderPositions, impassable, otherEnemies);
            foreach (DataBinding<ModelData> d in slotAssigned) movedDefenders.Add(d);

            // --- Phase 2: fallback for defenders no slot could take — the pre-#330 straight-line step
            // toward the nearest charger, clamped at the first obstruction. Same rule floor as before:
            // "as close as possible" caps out at zero when terrain blocks the lane.
            foreach (var (defender, targetCharger, currentB2B) in candidates)
            {
                if (movedDefenders.Contains(defender)) continue;

                ModelData defenderModel = defender.GetValue();
                Position defenderPos = workingDefenderPositions[defender];

                ModelData chargerModel = targetCharger.GetValue();
                float dx = chargerModel.Position.x - defenderPos.x;
                float dz = chargerModel.Position.z - defenderPos.z;
                float centerDist = MathF.Sqrt(dx * dx + dz * dz);
                if (centerDist < 1e-5f) continue;
                float dirX = dx / centerDist;
                float dirZ = dz / centerDist;

                // Cap: 3", or distance to BTB with target charger, whichever is smaller.
                float step = MathF.Min(MAX_PILE_IN_DISTANCE_INCHES, currentB2B);

                // Don't overlap other models. Target charger is exempt — we're moving toward it (we cap the
                // step at contact with it via currentB2B). Every OTHER model — other chargers, unit-mates, and
                // any other enemy unit (#159) — clamps the step so the defender stops at contact, not overlap.
                step = LimitStepByObstructions(defenderPos, defenderModel.BaseShape, defenderModel.Facing, dirX, dirZ, step,
                    defender, targetCharger, liveChargers, liveDefenders, workingDefenderPositions, otherEnemies);

                if (step < MIN_MEANINGFUL_STEP_INCHES) continue;

                Position tentative = new Position(defenderPos.x + dirX * step, defenderPos.z + dirZ * step);

                // Impassable terrain: if the move sweeps the model's base through any impassable piece, skip
                // pile-in for this model. Rule's "as close as possible" caps out at zero in this case.
                if (PathCrossesImpassable(defenderPos, tentative, defenderModel.BaseShape, defenderModel.Facing, impassable)) continue;

                workingDefenderPositions[defender] = tentative;
                movedDefenders.Add(defender);
            }

            // Strict coherency: greedily revert the worst offending move until coherent (or nothing left to revert).
            while (movedDefenders.Count > 0
                && !IsCoherent(liveDefenders, workingDefenderPositions, out DataBinding<ModelData>? worstMoved, movedDefenders))
            {
                if (worstMoved == null) break;
                workingDefenderPositions[worstMoved] = worstMoved.GetValue().Position;
                movedDefenders.Remove(worstMoved);
            }

            // #330 belt-and-braces: a revert can drop a defender back onto a spot another mover has since
            // been placed against (its vacated start looked free while it was elsewhere). Every emitted
            // position must be overlap-free, so revert the deepest-overlapping mover until clean. Bounded:
            // each pass removes one mover; the floor (nobody moved) is the original, valid layout.
            RevertResidualOverlaps(liveChargers, liveDefenders, workingDefenderPositions, otherEnemies, movedDefenders);

            List<PileInMove> result = new List<PileInMove>();
            foreach (DataBinding<ModelData> defender in movedDefenders)
            {
                Position newPos = workingDefenderPositions[defender];
                Position startPos = defender.GetValue().Position;
                float ddx = newPos.x - startPos.x;
                float ddz = newPos.z - startPos.z;
                if (ddx * ddx + ddz * ddz >= MIN_MEANINGFUL_STEP_INCHES * MIN_MEANINGFUL_STEP_INCHES)
                    result.Add(new PileInMove(defender, newPos));
            }
            return result;
        }

        /// <summary>Count of live defenders in base contact (within <see cref="BTB_EPSILON_INCHES"/> b2b)
        /// with any live charger. Read-only; used by the stage for the pile-in log line.</summary>
        public static int CountDefendersInBaseContact(
            IReadOnlyList<DataBinding<ModelData>> chargingModels,
            IReadOnlyList<DataBinding<ModelData>> defendingModels)
        {
            List<DataBinding<ModelData>> liveChargers = chargingModels
                .Where(m => m.GetValue().GetIsAlive()).ToList();
            int count = 0;
            foreach (DataBinding<ModelData> d in defendingModels)
            {
                ModelData dm = d.GetValue();
                if (!((IModel)dm).GetIsAlive()) continue;
                if (NearestB2BAt(dm.Position, dm.BaseShape, dm.Facing, liveChargers, out _) <= BTB_EPSILON_INCHES)
                    count++;
            }
            return count;
        }

        // --- #330 contact-slot assignment ------------------------------------------------------------

        private readonly struct ContactSlot
        {
            public readonly Position Pos;
            public readonly int Order; // (chargerIndex * SLOT_DIRECTIONS_PER_CHARGER) + angleIndex — the deterministic tie-break.

            public ContactSlot(Position pos, int order)
            {
                Pos = pos;
                Order = order;
            }
        }

        /// <summary>
        /// Greedily assigns candidate defenders to contact slots around the charging models, writing accepted
        /// placements into <paramref name="workingDefenderPositions"/> and returning the assigned defenders.
        /// Most-constrained defender picks first (fewest viable slots), so the model with one open flank takes
        /// it before a freer teammate does; ties break toward the defender already closest to contact, then
        /// input order. Every placement is re-validated against the positions accepted so far, so two
        /// defenders can never take overlapping slots.
        /// </summary>
        private static List<DataBinding<ModelData>> AssignDefendersToContactSlots(
            List<(DataBinding<ModelData> Defender, DataBinding<ModelData> Charger, float B2B)> candidates,
            List<DataBinding<ModelData>> liveChargers,
            List<DataBinding<ModelData>> liveDefenders,
            Dictionary<DataBinding<ModelData>, Position> workingDefenderPositions,
            List<ITerrain> impassable,
            IReadOnlyList<EnemyModelFootprint> otherEnemies)
        {
            var assigned = new List<DataBinding<ModelData>>();
            if (candidates.Count == 0) return assigned;

            // Slot rings per distinct defender (shape, facing) group. Units are usually homogeneous, so this
            // is one ring set; a mixed unit (joined hero on a bigger base) gets one per distinct base.
            var slotsByDefender = new Dictionary<DataBinding<ModelData>, List<ContactSlot>>();
            var slotCache = new List<(IBaseShape Shape, Float2 Facing, List<ContactSlot> Slots)>();
            foreach (var c in candidates)
            {
                ModelData dm = c.Defender.GetValue();
                List<ContactSlot>? slots = null;
                foreach (var entry in slotCache)
                {
                    if (SameShape(entry.Shape, dm.BaseShape) && entry.Facing.X == dm.Facing.X && entry.Facing.Y == dm.Facing.Y)
                    {
                        slots = entry.Slots;
                        break;
                    }
                }
                if (slots == null)
                {
                    slots = GenerateContactSlots(liveChargers, dm.BaseShape, dm.Facing);
                    slotCache.Add((dm.BaseShape, dm.Facing, slots));
                }
                slotsByDefender[c.Defender] = slots;
            }

            var unassigned = new List<(DataBinding<ModelData> Defender, DataBinding<ModelData> Charger, float B2B)>(candidates);

            // Each pass places exactly one defender, so the loop runs at most candidates.Count times.
            while (unassigned.Count > 0)
            {
                int bestIdx = -1;
                int bestViableCount = int.MaxValue;
                ContactSlot bestSlot = default;

                for (int i = 0; i < unassigned.Count; i++)
                {
                    var (defender, _, currentB2B) = unassigned[i];
                    ModelData dm = defender.GetValue();
                    Position from = dm.Position; // candidates never move before acceptance; working pos == start.

                    int viableCount = 0;
                    ContactSlot ownBest = default;
                    float ownBestDist = float.PositiveInfinity;

                    foreach (ContactSlot slot in slotsByDefender[defender])
                    {
                        float dx = slot.Pos.x - from.x;
                        float dz = slot.Pos.z - from.z;
                        float moveDist = MathF.Sqrt(dx * dx + dz * dz);
                        if (moveDist > MAX_PILE_IN_DISTANCE_INCHES) continue;
                        if (moveDist < MIN_MEANINGFUL_STEP_INCHES) continue; // standing in a slot means BTB already; not a candidate then.

                        if (!IsSlotEndStateFree(slot.Pos, dm, defender, liveChargers, liveDefenders,
                            workingDefenderPositions, otherEnemies)) continue;
                        if (!IsSlotPathClear(from, slot.Pos, moveDist, dm, defender, liveChargers,
                            impassable, otherEnemies)) continue;

                        viableCount++;
                        // Prefer the shortest move; tie -> lowest slot order. Strict '<' keeps it deterministic.
                        if (moveDist < ownBestDist - 1e-6f
                            || (MathF.Abs(moveDist - ownBestDist) <= 1e-6f && slot.Order < ownBest.Order))
                        {
                            ownBest = slot;
                            ownBestDist = moveDist;
                        }
                    }

                    if (viableCount == 0) continue;

                    // Most-constrained first; tie -> closest to contact already; tie -> input order (stable).
                    bool better = viableCount < bestViableCount
                        || (viableCount == bestViableCount && bestIdx >= 0 && currentB2B < unassigned[bestIdx].B2B - 1e-6f);
                    if (bestIdx < 0 || better)
                    {
                        bestIdx = i;
                        bestViableCount = viableCount;
                        bestSlot = ownBest;
                    }
                }

                if (bestIdx < 0) break; // nobody left can reach a free slot — the rest fall back.

                DataBinding<ModelData> placed = unassigned[bestIdx].Defender;
                workingDefenderPositions[placed] = bestSlot.Pos;
                assigned.Add(placed);
                unassigned.RemoveAt(bestIdx);
            }

            return assigned;
        }

        // Contact slots for a defender base of the given shape+facing, around every live charger:
        // SLOT_DIRECTIONS_PER_CHARGER rays out of each charger's center, each settled by binary search to the
        // center distance where the two bases sit SLOT_CONTACT_GAP_INCHES apart. Shape-agnostic: only
        // SurfaceGap2D is consulted, so circles, rectangles, and any future base measure identically.
        private static List<ContactSlot> GenerateContactSlots(
            List<DataBinding<ModelData>> liveChargers, IBaseShape defenderShape, Float2 defenderFacing)
        {
            var slots = new List<ContactSlot>(liveChargers.Count * SLOT_DIRECTIONS_PER_CHARGER);
            for (int c = 0; c < liveChargers.Count; c++)
            {
                ModelData cm = liveChargers[c].GetValue();
                float hi0 = cm.BaseShape.CircumscribedRadiusInches + defenderShape.CircumscribedRadiusInches
                    + SLOT_CONTACT_GAP_INCHES + 0.01f;

                for (int a = 0; a < SLOT_DIRECTIONS_PER_CHARGER; a++)
                {
                    float theta = a * (2f * MathF.PI / SLOT_DIRECTIONS_PER_CHARGER);
                    float dirX = MathF.Cos(theta);
                    float dirZ = MathF.Sin(theta);

                    // gap(d) is continuous, < 0 at d=0 (centers coincide) and > gap target at hi0 (bases fit
                    // inside their circumscribed circles), so the bisection always brackets a crossing.
                    float lo = 0f, hi = hi0;
                    for (int i = 0; i < 24; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        Position p = new Position(cm.Position.x + dirX * mid, cm.Position.z + dirZ * mid);
                        float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                            p, cm.Position, defenderShape, defenderFacing, cm.BaseShape, cm.Facing);
                        if (gap < SLOT_CONTACT_GAP_INCHES) lo = mid;
                        else hi = mid;
                    }

                    Position slotPos = new Position(cm.Position.x + dirX * hi, cm.Position.z + dirZ * hi);
                    float finalGap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                        slotPos, cm.Position, defenderShape, defenderFacing, cm.BaseShape, cm.Facing);
                    // Keep only well-formed slots: genuine base contact, never overlap. A degenerate search
                    // result is dropped here rather than trusted downstream.
                    if (finalGap < -0.001f || finalGap > BTB_EPSILON_INCHES) continue;

                    slots.Add(new ContactSlot(slotPos, c * SLOT_DIRECTIONS_PER_CHARGER + a));
                }
            }
            return slots;
        }

        // End state at a slot: the defender must not overlap ANY other base — chargers (contact is fine,
        // that's the point), teammates at their working positions, or any other enemy/friendly footprint.
        private static bool IsSlotEndStateFree(Position slotPos, ModelData defenderModel,
            DataBinding<ModelData> selfDefender,
            List<DataBinding<ModelData>> chargers,
            List<DataBinding<ModelData>> defenders,
            Dictionary<DataBinding<ModelData>, Position> workingDefenderPositions,
            IReadOnlyList<EnemyModelFootprint> otherEnemies)
        {
            foreach (DataBinding<ModelData> other in chargers)
            {
                ModelData om = other.GetValue();
                float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                    slotPos, om.Position, defenderModel.BaseShape, defenderModel.Facing, om.BaseShape, om.Facing);
                if (gap < -OVERLAP_TOLERANCE_INCHES) return false;
            }
            foreach (DataBinding<ModelData> other in defenders)
            {
                if (ReferenceEquals(other, selfDefender)) continue;
                ModelData om = other.GetValue();
                float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                    slotPos, workingDefenderPositions[other], defenderModel.BaseShape, defenderModel.Facing, om.BaseShape, om.Facing);
                if (gap < -OVERLAP_TOLERANCE_INCHES) return false;
            }
            foreach (EnemyModelFootprint enemy in otherEnemies)
            {
                float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                    slotPos, enemy.Center, defenderModel.BaseShape, defenderModel.Facing, enemy.BaseShape, enemy.Facing);
                if (gap < -OVERLAP_TOLERANCE_INCHES) return false;
            }
            return true;
        }

        // Path to a slot: the straight swept base must clear impassable terrain and every base that is NOT
        // moving in this pile-in — chargers (grazing the target at the final approach is expected; blocked
        // only when the touch comes meaningfully before the destination) and other enemy/friendly footprints.
        // Fellow defenders are deliberately NOT swept obstacles: the whole unit shuffles simultaneously, so a
        // model may pass through a teammate's start square — end-state overlap is still forbidden above. This
        // transparency is exactly what lets the second rank slide around the first into contact (#330).
        private static bool IsSlotPathClear(Position from, Position to, float moveDist, ModelData defenderModel,
            DataBinding<ModelData> selfDefender,
            List<DataBinding<ModelData>> chargers,
            List<ITerrain> impassable,
            IReadOnlyList<EnemyModelFootprint> otherEnemies)
        {
            if (PathCrossesImpassable(from, to, defenderModel.BaseShape, defenderModel.Facing, impassable))
                return false;

            float dirX = (to.x - from.x) / moveDist;
            float dirZ = (to.z - from.z) / moveDist;

            foreach (DataBinding<ModelData> other in chargers)
            {
                ModelData om = other.GetValue();
                float allowed = MaxStepToTouch(from, defenderModel.BaseShape, defenderModel.Facing, dirX, dirZ,
                    moveDist, om.Position, om.BaseShape, om.Facing);
                if (allowed < moveDist - PATH_TOUCH_TOLERANCE_INCHES) return false;
            }
            foreach (EnemyModelFootprint enemy in otherEnemies)
            {
                float allowed = MaxStepToTouch(from, defenderModel.BaseShape, defenderModel.Facing, dirX, dirZ,
                    moveDist, enemy.Center, enemy.BaseShape, enemy.Facing);
                if (allowed < moveDist - PATH_TOUCH_TOLERANCE_INCHES) return false;
            }
            return true;
        }

        private static bool SameShape(IBaseShape a, IBaseShape b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is CircleBase ca && b is CircleBase cb) return ca.RadiusInches == cb.RadiusInches;
            if (a is RectangleBase ra && b is RectangleBase rb)
                return ra.WidthInches == rb.WidthInches && ra.HeightInches == rb.HeightInches;
            return false;
        }

        // Final safety pass (#330): revert any moved defender whose final position overlaps another base
        // deeper than tolerance. Deepest overlap reverts first; each pass shrinks movedDefenders, so the loop
        // is bounded and the floor is the original (valid) layout.
        private static void RevertResidualOverlaps(
            List<DataBinding<ModelData>> liveChargers,
            List<DataBinding<ModelData>> liveDefenders,
            Dictionary<DataBinding<ModelData>, Position> workingDefenderPositions,
            IReadOnlyList<EnemyModelFootprint> otherEnemies,
            HashSet<DataBinding<ModelData>> movedDefenders)
        {
            while (movedDefenders.Count > 0)
            {
                DataBinding<ModelData>? worst = null;
                float worstDepth = OVERLAP_TOLERANCE_INCHES;

                foreach (DataBinding<ModelData> mover in movedDefenders)
                {
                    ModelData mm = mover.GetValue();
                    Position pos = workingDefenderPositions[mover];
                    float deepest = 0f;

                    foreach (DataBinding<ModelData> other in liveChargers)
                    {
                        ModelData om = other.GetValue();
                        float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                            pos, om.Position, mm.BaseShape, mm.Facing, om.BaseShape, om.Facing);
                        if (-gap > deepest) deepest = -gap;
                    }
                    foreach (DataBinding<ModelData> other in liveDefenders)
                    {
                        if (ReferenceEquals(other, mover)) continue;
                        ModelData om = other.GetValue();
                        float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                            pos, workingDefenderPositions[other], mm.BaseShape, mm.Facing, om.BaseShape, om.Facing);
                        if (-gap > deepest) deepest = -gap;
                    }
                    foreach (EnemyModelFootprint enemy in otherEnemies)
                    {
                        float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                            pos, enemy.Center, mm.BaseShape, mm.Facing, enemy.BaseShape, enemy.Facing);
                        if (-gap > deepest) deepest = -gap;
                    }

                    if (deepest > worstDepth)
                    {
                        worstDepth = deepest;
                        worst = mover;
                    }
                }

                if (worst == null) return; // no residual overlap — the common case, first pass.
                workingDefenderPositions[worst] = worst.GetValue().Position;
                movedDefenders.Remove(worst);
            }
        }

        private static float NearestB2BAt(Position pos, IBaseShape shape, Float2 facing,
            List<DataBinding<ModelData>> chargers, out DataBinding<ModelData>? nearest)
        {
            nearest = null;
            float bestB2B = float.PositiveInfinity;
            foreach (DataBinding<ModelData> c in chargers)
            {
                ModelData cm = c.GetValue();
                // True facing-aware base-to-base gap (#150/#159), so the pile-in step caps at real contact —
                // a facing-less gap over-estimates a rotated rectangle's reach and let the defender overshoot
                // into it. Circle-vs-circle is exactly the old radius form (facing is irrelevant for circles).
                float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                    pos, cm.Position, shape, facing, cm.BaseShape, cm.Facing);
                if (b2b < bestB2B) { bestB2B = b2b; nearest = c; }
            }
            return bestB2B;
        }

        // For each non-target obstruction, return the max step along (dirX, dirZ) before the moving model's
        // base touches it. Takes the min over all obstructions.
        private static float LimitStepByObstructions(Position from, IBaseShape movingShape, Float2 movingFacing,
            float dirX, float dirZ, float maxStep,
            DataBinding<ModelData> selfDefender,
            DataBinding<ModelData> targetCharger,
            List<DataBinding<ModelData>> chargers,
            List<DataBinding<ModelData>> defenders,
            Dictionary<DataBinding<ModelData>, Position> workingDefenderPositions,
            IReadOnlyList<EnemyModelFootprint> otherEnemies)
        {
            float allowed = maxStep;

            foreach (DataBinding<ModelData> other in chargers)
            {
                if (ReferenceEquals(other, targetCharger)) continue;
                ModelData om = other.GetValue();
                allowed = MathF.Min(allowed, MaxStepToTouch(from, movingShape, movingFacing, dirX, dirZ, allowed,
                    om.Position, om.BaseShape, om.Facing));
            }
            foreach (DataBinding<ModelData> other in defenders)
            {
                if (ReferenceEquals(other, selfDefender)) continue;
                ModelData om = other.GetValue();
                Position otherPos = workingDefenderPositions[other];
                allowed = MathF.Min(allowed, MaxStepToTouch(from, movingShape, movingFacing, dirX, dirZ, allowed,
                    otherPos, om.BaseShape, om.Facing));
            }
            // #159: any OTHER enemy unit's model (not the charging unit) is a hard obstacle — a defender may not
            // pile through it. Stops the defender at contact with a third-party / already-engaged enemy base.
            foreach (EnemyModelFootprint enemy in otherEnemies)
            {
                allowed = MathF.Min(allowed, MaxStepToTouch(from, movingShape, movingFacing, dirX, dirZ, allowed,
                    enemy.Center, enemy.BaseShape, enemy.Facing));
            }
            return MathF.Max(0f, allowed);
        }

        // Max distance the moving base (at `from`, along unit dir) can travel before it touches the static obstacle
        // base at obstPos, capped at `upperBound`. Circle-vs-circle keeps the exact closed-form ray-vs-circle
        // (unchanged); any rectangle involved binary-searches the swept footprint intersection (#150).
        private static float MaxStepToTouch(Position from, IBaseShape movingShape, Float2 movingFacing,
            float dirX, float dirZ, float upperBound, Position obstPos, IBaseShape obstShape, Float2 obstFacing)
        {
            if (movingShape is CircleBase mc && obstShape is CircleBase oc)
            {
                float dx = obstPos.x - from.x;
                float dz = obstPos.z - from.z;
                float proj = dx * dirX + dz * dirZ;
                if (proj <= 0f) return float.PositiveInfinity;
                float distSq = dx * dx + dz * dz;
                float perpSq = distSq - proj * proj;
                float combined = mc.RadiusInches + oc.RadiusInches;
                float combinedSq = combined * combined;
                if (perpSq >= combinedSq) return float.PositiveInfinity;
                float behind = MathF.Sqrt(combinedSq - perpSq);
                // Subtract a tiny margin so float drift doesn't leave bases overlapping after the move.
                return MathF.Max(0f, proj - behind - 0.001f);
            }

            if (upperBound <= 0f) return 0f;
            IZone obstZone = obstShape.ToZone(obstPos, obstFacing);
            Float2 origin = new Float2(from.x, from.z);
            // The swept-footprint overlap grows monotonically with travel, so binary-search the transition.
            if (SweptBaseGeometry.DoesSweptBaseIntersectZone(obstZone, origin, origin, movingShape, movingFacing))
                return 0f; // already touching where it stands
            Float2 farEnd = new Float2(from.x + dirX * upperBound, from.z + dirZ * upperBound);
            if (!SweptBaseGeometry.DoesSweptBaseIntersectZone(obstZone, origin, farEnd, movingShape, movingFacing))
                return upperBound; // never reaches the obstacle within the bound
            float lo = 0f, hi = upperBound;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                Float2 end = new Float2(from.x + dirX * mid, from.z + dirZ * mid);
                if (SweptBaseGeometry.DoesSweptBaseIntersectZone(obstZone, origin, end, movingShape, movingFacing)) hi = mid;
                else lo = mid;
            }
            return MathF.Max(0f, lo - 0.001f);
        }

        // Sweeps the moving base's true footprint (not a point) from `from` to `to` against each impassible
        // piece (#150) — a rectangular base can no longer clip a corner of terrain undetected. A circular base
        // reduces to the swept disc.
        private static bool PathCrossesImpassable(Position from, Position to, IBaseShape movingShape, Float2 movingFacing,
            List<ITerrain> impassable)
        {
            if (impassable.Count == 0) return false;
            Float2 a = new Float2(from.x, from.z);
            Float2 b = new Float2(to.x, to.z);
            foreach (ITerrain piece in impassable)
            {
                if (SweptBaseGeometry.DoesSweptBaseIntersectZone(piece.Shape, a, b, movingShape, movingFacing)) return true;
            }
            return false;
        }

        // Coherency: every model within 1" of nearest unit-mate; every pair within 9". When violated,
        // also nominates the *moved* defender that most contributes to the violation, so the caller can
        // revert it. If no moved defender is involved in the violation, returns false with worstMoved=null
        // and the caller should bail out (the violation pre-existed or only involves non-moved models).
        private static bool IsCoherent(IReadOnlyList<DataBinding<ModelData>> defenders,
            Dictionary<DataBinding<ModelData>, Position> positions,
            out DataBinding<ModelData>? worstMoved,
            HashSet<DataBinding<ModelData>> movedDefenders)
        {
            worstMoved = null;
            if (defenders.Count <= 1) return true;

            bool anyViolation = false;
            float worstExcess = 0f;

            // Nearest-neighbor (1") rule: for each model whose nearest unit-mate is >1", consider both
            // ends of that pair as revert candidates. Prefer the moved one; on tie, the one that moved
            // farther (more responsible for the stretch).
            for (int i = 0; i < defenders.Count; i++)
            {
                DataBinding<ModelData> di = defenders[i];
                ModelData mi = di.GetValue();
                Position pi = positions[di];
                float nearest = float.PositiveInfinity;
                int nearestIdx = -1;
                for (int j = 0; j < defenders.Count; j++)
                {
                    if (i == j) continue;
                    DataBinding<ModelData> dj = defenders[j];
                    float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                        pi, positions[dj], mi.BaseShape, mi.Facing, dj.GetValue().BaseShape, dj.GetValue().Facing);
                    if (d < nearest) { nearest = d; nearestIdx = j; }
                }
                float excess = nearest - GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES;
                if (excess > 0f)
                {
                    anyViolation = true;
                    DataBinding<ModelData>? candidate = PickRevertCandidate(di, defenders[nearestIdx], movedDefenders, positions);
                    if (candidate != null && excess > worstExcess)
                    {
                        worstExcess = excess;
                        worstMoved = candidate;
                    }
                }
            }

            // Far-pair (9") rule: any pair >9" b2b is a violation.
            for (int i = 0; i < defenders.Count; i++)
            {
                for (int j = i + 1; j < defenders.Count; j++)
                {
                    DataBinding<ModelData> di = defenders[i];
                    DataBinding<ModelData> dj = defenders[j];
                    float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                        positions[di], positions[dj],
                        di.GetValue().BaseShape, di.GetValue().Facing, dj.GetValue().BaseShape, dj.GetValue().Facing);
                    float excess = d - GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES;
                    if (excess > 0f)
                    {
                        anyViolation = true;
                        DataBinding<ModelData>? candidate = PickRevertCandidate(di, dj, movedDefenders, positions);
                        if (candidate != null && excess > worstExcess)
                        {
                            worstExcess = excess;
                            worstMoved = candidate;
                        }
                    }
                }
            }

            return !anyViolation;
        }

        // For a violating pair (a, b), pick whichever is in movedDefenders to revert. If both, pick the one
        // whose move-from-start is largest. If neither, return null — caller will bail out.
        private static DataBinding<ModelData>? PickRevertCandidate(DataBinding<ModelData> a, DataBinding<ModelData> b,
            HashSet<DataBinding<ModelData>> movedDefenders,
            Dictionary<DataBinding<ModelData>, Position> positions)
        {
            bool aMoved = movedDefenders.Contains(a);
            bool bMoved = movedDefenders.Contains(b);
            if (!aMoved && !bMoved) return null;
            if (aMoved && !bMoved) return a;
            if (bMoved && !aMoved) return b;
            return MoveDistance(a, positions) >= MoveDistance(b, positions) ? a : b;
        }

        private static float MoveDistance(DataBinding<ModelData> model, Dictionary<DataBinding<ModelData>, Position> positions)
        {
            Position start = model.GetValue().Position;
            Position now = positions[model];
            float dx = now.x - start.x;
            float dz = now.z - start.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
