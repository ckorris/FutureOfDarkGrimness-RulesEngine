using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// Computes the defender's reactive pile-in moves after a charge. GF v3.5.1 p.9:
    /// defender models not in base contact must move by up to 3" toward a charging model,
    /// or as close as possible, maintaining unit coherency.
    /// </summary>
    public static class PileInUtilities
    {
        public const float MAX_PILE_IN_DISTANCE_INCHES = 3f;

        // Anything closer than this base-to-base counts as already in BTB. Guards against float drift.
        private const float BTB_EPSILON_INCHES = 0.01f;

        // Don't bother applying a move shorter than this; reduces noise.
        private const float MIN_MEANINGFUL_STEP_INCHES = 0.01f;

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

            foreach (var (defender, targetCharger, currentB2B) in candidates)
            {
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
