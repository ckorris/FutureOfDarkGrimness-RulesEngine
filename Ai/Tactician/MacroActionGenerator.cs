using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Enumerates goal-directed macro-action candidates for one unit's activation (#191 A3c,
    /// Appendix A v2 as confirmed). Geometry, never sampling (plan D5): every goal is a named point
    /// (an objective, a range band, a lane midpoint), every move is produced by the MovementPlanner
    /// ladder and is therefore one the engine accepts (G3).
    /// <para>
    /// Pruning is diversity-preserving (Appendix A generator rule): the budget ranks candidates by
    /// simple goal-progress WITHIN a family, but at least one feasible candidate of every family
    /// survives, and nothing is pruned purely by immediate expected value - sacrificial plays (fatigue
    /// bait, throwaway blocks) must reach search. Sub-slice note: M11 MoveToCast / M12 DeliverCargo
    /// land with A3c-2 (they need the casting/transport queries).
    /// </para>
    /// </summary>
    public static class MacroActionGenerator
    {
        /// <summary>Ranking budget (Appendix A: "the cap becomes a ranking budget"). Tuned at B2.</summary>
        public const int DefaultCandidateBudget = 16;

        /// <summary>Enemies considered per targeted family, by unit value - keeps enumeration O(small).</summary>
        private const int TopEnemies = 3;

        private const float ContactFeasibleGapInches = 0.25f;
        private const float BandMarginInches = 0.5f;

        public static List<MacroAction> Enumerate(RuleEvaluator evaluator, ITableState tableState,
            DataBinding<UnitData> unit, int candidateBudget = DefaultCandidateBudget)
        {
            UnitData self = unit.GetValue();
            var living = self.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            var candidates = new List<MacroAction>();

            // M1 Hold - always present, always feasible (the planner's stay is cohesion-safe).
            candidates.Add(new MacroAction(EMacroIntent.Hold, "intent=Hold", EActionType.Hold,
                MovementPlanner.StayInPlace(unit), EFeasibility.Reachable, Centroid(living, self)));

            if (living.Count == 0) return candidates;

            float advance = TacticalAnalysis.AdvanceDistance(self, evaluator);
            float rush = TacticalAnalysis.RushDistance(self, evaluator);
            bool canMoveThroughEnemies = MovementRuleQueries.CanMoveThroughEnemies(self, evaluator);
            bool ignoresDifficult = MovementRuleQueries.IgnoresDifficultTerrain(self, evaluator);
            bool ignoresAllTerrain = MovementRuleQueries.IgnoresAllTerrain(self, evaluator);

            Position start = Centroid(living, self);
            List<IUnit> enemies = LivingEnemies(tableState, self.PlayerID);
            List<IUnit> friends = LivingFriends(tableState, self);

            // M2/M3 - objectives, both budgets (rush reaches farther; ranking keeps the useful one).
            foreach (IObjective objective in tableState.Objectives.Objects)
            {
                candidates.Add(Plan(EMacroIntent.AdvanceOnObjective,
                    $"intent=AdvanceOnObjective obj=({objective.Position.x:F0},{objective.Position.z:F0})",
                    EActionType.Advance, unit, living, tableState, evaluator, objective.Position, advance,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain,
                    goalRadius: TacticalAnalysis.ObjectiveSeizureRadiusInches,
                    targetObjective: objective));
                candidates.Add(Plan(EMacroIntent.RushObjective,
                    $"intent=RushObjective obj=({objective.Position.x:F0},{objective.Position.z:F0})",
                    EActionType.Rush, unit, living, tableState, evaluator, objective.Position, rush,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain,
                    goalRadius: TacticalAnalysis.ObjectiveSeizureRadiusInches,
                    targetObjective: objective));
            }

            List<IUnit> rankedEnemies = enemies
                .OrderByDescending(TacticalAnalysis.UnitValue).Take(TopEnemies).ToList();
            bool hasRanged = self.GetRangedWeapons().Count > 0;
            bool hasMelee = self.GetMeleeWeapons().Count > 0;
            var terrain = tableState.Terrain.Objects.ToList();
            var enemyFootprints = MovementPlanner.LiveEnemyFootprints(tableState, self.PlayerID);
            float leadRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);

            foreach (IUnit enemy in rankedEnemies)
            {
                Position enemyPos = Centroid(enemy);

                // M4 - range bands (shooters only; Advance so the unit can still shoot).
                if (hasRanged)
                {
                    float reach = TacticalAnalysis.MaxWeaponRange(self, enemy, evaluator);
                    foreach (ERangeBand band in BandsFor(self, enemy, reach, evaluator, out float[] distances))
                    {
                        float d = distances[(int)band];
                        Position goal = PointAtDistanceFrom(enemyPos, start, d);
                        candidates.Add(Plan(EMacroIntent.EngageAtRange,
                            $"intent=EngageAtRange band={band} target={enemy.Name} d={d:F1}",
                            EActionType.Advance, unit, living, tableState, evaluator, goal, advance,
                            canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain,
                            goalRadius: BandMarginInches, targetEnemy: enemy, band: band));
                    }
                }

                // M5 - charge to contact (generated even when the exchange looks bad - diversity rule).
                if (hasMelee)
                {
                    candidates.Add(BuildCharge(unit, living, tableState, evaluator, self, enemy,
                        start, leadRadius, terrain, enemyFootprints,
                        canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain));
                }
            }

            // M6 - fall back from the biggest threat.
            if (enemies.Count > 0)
            {
                IUnit threat = rankedEnemies[0];
                Position threatPos = Centroid(threat);
                Position away = PointAtDistanceFrom(threatPos, start,
                    Distance(threatPos, start) + rush);
                candidates.Add(Plan(EMacroIntent.FallBack,
                    $"intent=FallBack from={threat.Name}", EActionType.Rush,
                    unit, living, tableState, evaluator, ClampToTable(away), rush,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 1f,
                    targetEnemy: threat));
            }

            // M7 - seek cover from the biggest threat, behind the nearest cover piece in reach.
            if (enemies.Count > 0 && TryFindCoverGoal(tableState, start, Centroid(rankedEnemies[0]),
                    rush, out Position coverGoal))
            {
                candidates.Add(Plan(EMacroIntent.SeekCoverFrom,
                    $"intent=SeekCoverFrom from={rankedEnemies[0].Name}", EActionType.Rush,
                    unit, living, tableState, evaluator, coverGoal, rush,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 1f,
                    targetEnemy: rankedEnemies[0]));
            }

            // M8 - block the biggest threat's lane to our most valuable asset (a LINE across it).
            if (enemies.Count > 0)
            {
                IUnit threat = rankedEnemies[0];
                Position? asset = MostValuableAssetPosition(tableState, self, friends);
                if (asset != null)
                {
                    Position threatPos2 = Centroid(threat);
                    Position lane = Midpoint(threatPos2, asset.Value);
                    // The barrier spreads PERPENDICULAR to the lane it walls, whatever direction the
                    // blocker approaches from.
                    float laneDx = asset.Value.x - threatPos2.x, laneDz = asset.Value.z - threatPos2.z;
                    candidates.Add(Plan(EMacroIntent.Block,
                        $"intent=Block enemy={threat.Name}", EActionType.Rush,
                        unit, living, tableState, evaluator, lane, rush,
                        canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 1f,
                        targetEnemy: threat, formation: MovementPlanner.EFormation.Line,
                        lineAxis: (-laneDz, laneDx)));
                }
            }

            // M9 - escort the most valuable OTHER friendly unit, interposing toward its threat.
            if (friends.Count > 0 && enemies.Count > 0)
            {
                IUnit ward = friends.OrderByDescending(TacticalAnalysis.UnitValue).First();
                Position wardPos = Centroid(ward);
                IUnit wardThreat = enemies.OrderBy(e => Distance(Centroid(e), wardPos)).First();
                Position goal = PointAtDistanceFrom(wardPos, Centroid(wardThreat), -2.5f);
                candidates.Add(Plan(EMacroIntent.Escort,
                    $"intent=Escort ally={ward.Name}", EActionType.Rush,
                    unit, living, tableState, evaluator, ClampToTable(goal), rush,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 2f,
                    targetAlly: ward));
            }

            // M10 - concentrate on the rest of the army's centroid.
            if (friends.Count > 0)
            {
                Position mass = MeanPosition(friends.Select(Centroid).ToList());
                candidates.Add(Plan(EMacroIntent.Concentrate,
                    "intent=Concentrate", EActionType.Rush,
                    unit, living, tableState, evaluator, mass, rush,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 3f));
            }

            return PruneWithDiversity(candidates, candidateBudget);
        }

        /// <summary>
        /// M5 via the solo resolver's proven construction when the straight lane is clear: aim an
        /// explicit end gap and refine (packing the centroid ONTO the contact point would overlap
        /// bases and back the whole move off); route around terrain via the path planner otherwise.
        /// Feasibility is graded by the ACHIEVED gap - contact is the intent.
        /// </summary>
        private static MacroAction BuildCharge(DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, ITableState tableState, RuleEvaluator evaluator,
            UnitData self, IUnit enemy, Position start, float leadRadius,
            List<ITerrain> terrain, List<EnemyModelFootprint> enemyFootprints,
            bool canMoveThroughEnemies, bool ignoresDifficult, bool ignoresAllTerrain)
        {
            float chargeReach = Math.Max(0f,
                TacticalAnalysis.ChargeDistanceAgainst(self, enemy, evaluator) - 0.001f);
            IModel? nearestModel = NearestLivingModel(enemy, start);
            Position enemyPos = nearestModel?.Position ?? Centroid(enemy);
            float contactDistance = leadRadius + (nearestModel?.BaseRadiusInches ?? 0.5f);
            string rationale = $"intent=ChargeToContact target={enemy.Name}";

            List<ModelMoveEntry> move;
            bool straightClear = ignoresAllTerrain || !terrain.Any(t =>
                t.TerrainType.HasFlag(ETerrainType.Impassible)
                && t.Shape.DoesPathIntersectZone(new Float2(start.x, start.z),
                    new Float2(enemyPos.x, enemyPos.z), leadRadius));
            if (straightClear)
            {
                float dx = enemyPos.x - start.x, dz = enemyPos.z - start.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                (float ndx, float ndz) = dist < 1e-6f ? (1f, 0f) : (dx / dist, dz / dist);
                float initialStep = Math.Clamp(
                    dist - contactDistance - MovementPlanner.ChargeContactTargetGapInches, 0f, chargeReach);
                float step = MovementPlanner.RefineStepTowardGap(unit, living, start.x, start.z,
                    ndx, ndz, initialStep, chargeReach,
                    MovementPlanner.ChargeContactTargetGapInches, enemyFootprints, chargeReach);
                move = MovementPlanner.ValidateWithBackoff(
                    s => MovementPlanner.BuildCandidate(unit, living, start.x, start.z, ndx, ndz, s, chargeReach),
                    step, unit, living, _ => new ModelMoveBudget(chargeReach, chargeReach),
                    enemyFootprints, canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, terrain);
            }
            else
            {
                Position contactGoal = PointAtDistanceFrom(enemyPos, start,
                    contactDistance + MovementPlanner.ChargeContactTargetGapInches);
                move = MovementPlanner.PlanMoveToward(unit, living, tableState, contactGoal,
                    chargeReach, chargeReach, _ => new ModelMoveBudget(chargeReach, chargeReach),
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain);
            }

            float gap = MovementPlanner.MinEnemyGap(move, enemyFootprints);
            Position end = MoveCentroid(move, living);
            float progress = Distance(start, enemyPos) - Distance(end, enemyPos);
            EFeasibility feasibility = gap <= ContactFeasibleGapInches
                ? EFeasibility.Reachable
                : progress > 0.25f ? EFeasibility.BudgetClipped : EFeasibility.Blocked;

            return new MacroAction(EMacroIntent.ChargeToContact, rationale, EActionType.Charge,
                move, feasibility, end, TargetEnemy: enemy);
        }

        // --- planning core ------------------------------------------------------------------------

        private static MacroAction Plan(EMacroIntent intent, string rationale, EActionType actionType,
            DataBinding<UnitData> unit, List<DataBinding<ModelData>> living, ITableState tableState,
            RuleEvaluator evaluator, Position goal, float budget,
            bool canMoveThroughEnemies, bool ignoresDifficult, bool ignoresAllTerrain,
            float goalRadius, IUnit? targetEnemy = null, IObjective? targetObjective = null,
            IUnit? targetAlly = null, ERangeBand? band = null,
            MovementPlanner.EFormation formation = MovementPlanner.EFormation.Grid,
            (float X, float Z)? lineAxis = null)
        {
            float safeBudget = Math.Max(0f, budget - 0.001f); // the ResolverGuide float-precision margin
            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(unit, living, tableState, goal,
                safeBudget, safeBudget, _ => new ModelMoveBudget(safeBudget, safeBudget),
                canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, formation, lineAxis);

            Position end = MoveCentroid(move, living);
            Position start = Centroid(living, unit.GetValue());
            float progress = Distance(start, goal) - Distance(end, goal);

            EFeasibility feasibility = Distance(end, goal) <= goalRadius
                ? EFeasibility.Reachable
                : progress > 0.25f ? EFeasibility.BudgetClipped : EFeasibility.Blocked;

            return new MacroAction(intent, rationale, actionType, move, feasibility, end,
                targetEnemy, targetObjective, targetAlly, band);
        }

        /// <summary>
        /// Diversity-preserving pruning (Appendix A): rank by goal progress WITHIN each family, then
        /// round-robin across families so every feasible family keeps at least one candidate before
        /// any family gets its second. Never ranks across families by value - that is search's job.
        /// </summary>
        private static List<MacroAction> PruneWithDiversity(List<MacroAction> candidates, int budget)
        {
            var byFamily = candidates
                .GroupBy(c => c.Intent)
                .Select(g => g.OrderByDescending(FeasibilityRank).ToList())
                .ToList();

            var kept = new List<MacroAction>();
            for (int round = 0; ; round++)
            {
                bool anyLeft = false;
                foreach (List<MacroAction> family in byFamily)
                {
                    if (round >= family.Count) continue;
                    anyLeft = true;
                    // Round 0 always completes - one candidate per family survives even past the
                    // budget (the diversity guarantee). Later rounds respect the budget.
                    if (round == 0 || kept.Count < budget)
                        kept.Add(family[round]);
                }
                if (!anyLeft || (round > 0 && kept.Count >= budget)) break;
            }
            return kept;
        }

        private static int FeasibilityRank(MacroAction action) => action.Feasibility switch
        {
            EFeasibility.Reachable => 2,
            EFeasibility.BudgetClipped => 1,
            _ => 0,
        };

        // --- M4 band geometry -----------------------------------------------------------------------

        private static IEnumerable<ERangeBand> BandsFor(UnitData self, IUnit enemy, float reach,
            RuleEvaluator evaluator, out float[] distances)
        {
            distances = new float[3];
            distances[(int)ERangeBand.MaxRange] = Math.Max(1f, reach - BandMarginInches);
            distances[(int)ERangeBand.HalfRange] = Math.Max(1f, reach / 2f);

            var bands = new List<ERangeBand> { ERangeBand.MaxRange, ERangeBand.HalfRange };

            // Kite band: outside the enemy's whole-activation threat, inside our reach. Only exists
            // when those overlap; may move AWAY from the enemy - that is the point.
            float enemyThreat = TacticalAnalysis.ThreatRangeAgainst(enemy, self, evaluator);
            float safe = enemyThreat + BandMarginInches;
            if (safe < reach - BandMarginInches)
            {
                distances[(int)ERangeBand.SafeShooting] = safe;
                bands.Add(ERangeBand.SafeShooting);
            }
            return bands;
        }

        // --- M7 cover geometry ------------------------------------------------------------------------

        private static bool TryFindCoverGoal(ITableState tableState, Position start, Position threat,
            float reach, out Position goal)
        {
            goal = default;
            float best = float.MaxValue;
            foreach (ITerrain piece in tableState.Terrain.Objects)
            {
                if (!piece.TerrainType.HasFlag(ETerrainType.Cover)) continue;
                if (piece.Shape is not IBoundedZone bounded) continue;

                var center = new Position(bounded.Bounds.CenterX, bounded.Bounds.CenterZ);
                // The far side of the piece relative to the threat, one inch clear of its footprint.
                float halfExtent = Math.Max(bounded.Bounds.Right - bounded.Bounds.Left,
                    bounded.Bounds.Top - bounded.Bounds.Bottom) / 2f;
                Position behind = PointAtDistanceFrom(threat, center,
                    Distance(threat, center) + halfExtent + 1f);

                float travel = Distance(start, behind);
                if (travel > reach + 6f) continue; // hopeless this activation; skip (Blocked anyway)
                if (travel < best)
                {
                    best = travel;
                    goal = ClampToTable(behind);
                }
            }
            return best < float.MaxValue;
        }

        // --- M8 asset choice ----------------------------------------------------------------------------

        private static Position? MostValuableAssetPosition(ITableState tableState, UnitData self,
            List<IUnit> friends)
        {
            if (friends.Count > 0)
                return Centroid(friends.OrderByDescending(TacticalAnalysis.UnitValue).First());

            IObjective? owned = tableState.Objectives.Objects
                .FirstOrDefault(o => o.OwnerID.HasValue && o.OwnerID.Value == self.PlayerID);
            return owned?.Position;
        }

        // --- geometry helpers ------------------------------------------------------------------------

        /// <summary>A point at <paramref name="distance"/> from <paramref name="anchor"/> along the direction toward <paramref name="toward"/>.</summary>
        private static Position PointAtDistanceFrom(Position anchor, Position toward, float distance)
        {
            float dx = toward.x - anchor.x, dz = toward.z - anchor.z;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6f) { dx = 1f; dz = 0f; len = 1f; }
            return new Position(anchor.x + dx / len * distance, anchor.z + dz / len * distance);
        }

        private static Position ClampToTable(Position p) => new Position(
            Math.Clamp(p.x, 1f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES - 1f),
            Math.Clamp(p.z, 1f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - 1f));

        private static Position Midpoint(Position a, Position b) =>
            new Position((a.x + b.x) / 2f, (a.z + b.z) / 2f);

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static Position Centroid(IUnit unit)
        {
            var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (alive.Count == 0) return new Position(0f, 0f);
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        private static Position Centroid(List<DataBinding<ModelData>> living, UnitData fallback)
        {
            if (living.Count == 0) return Centroid((IUnit)fallback);
            return new Position(
                living.Average(mb => mb.GetValue().Position.x),
                living.Average(mb => mb.GetValue().Position.z));
        }

        private static Position MoveCentroid(List<ModelMoveEntry> move, List<DataBinding<ModelData>> living)
        {
            var ends = move.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0)
                ends = living.Select(mb => mb.GetValue().Position).ToList();
            return MeanPosition(ends);
        }

        private static Position MeanPosition(List<Position> points) =>
            new Position(points.Average(p => p.x), points.Average(p => p.z));

        private static IModel? NearestLivingModel(IUnit enemy, Position from) =>
            enemy.Models.Where(m => m.GetIsAlive())
                .OrderBy(m => Distance(m.Position, from)).FirstOrDefault();

        private static List<IUnit> LivingEnemies(ITableState tableState, PlayerID self) =>
            tableState.Units.Objects
                .Where(u => u.PlayerID != self && u.Models.Any(m => m.GetIsAlive()))
                .ToList();

        private static List<IUnit> LivingFriends(ITableState tableState, UnitData self) =>
            tableState.Units.Objects
                .Where(u => u.PlayerID == self.PlayerID && !ReferenceEquals(u, self)
                    && u.Models.Any(m => m.GetIsAlive()))
                .ToList();
    }
}
