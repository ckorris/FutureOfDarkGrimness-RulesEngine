using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
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
        // Non-charge moves must end >= 1" from enemies (engine standoff rule); aim with slack.
        private const float ApproachStandoffGapInches = 1.1f;

        /// <summary>
        /// A move allowance for planning (#264 issue 7): how far to plan - the SLOWEST model's cap, so
        /// the whole unit can make the move - plus each model's OWN cap for validation. Planning at
        /// the unit scalar and validating everyone against it is what made a joined Slow hero's unit
        /// fail the movement resolver's per-model re-check on every candidate.
        /// </summary>
        private readonly record struct PlanBudget(
            float Inches, Func<ModelMoveEntry, ModelMoveBudget> PerModel);

        private static Func<ModelMoveEntry, ModelMoveBudget> PerModelCaps(
            IReadOnlyDictionary<ModelID, (float Advance, float Rush, float Charge)> budgets,
            EActionType actionType, float fallback) => entry =>
            {
                float cap = fallback;
                if (budgets.TryGetValue(entry.Model.GetValue().ID,
                        out (float Advance, float Rush, float Charge) budget))
                {
                    cap = actionType switch
                    {
                        EActionType.Advance => budget.Advance,
                        EActionType.Charge => budget.Charge,
                        _ => budget.Rush,
                    };
                }
                return new ModelMoveBudget(cap, cap);
            };

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

            // #264 issue 7: plan at the SLOWEST model's allowance and validate each model against its
            // own. The unit scalars take the MAX across models (a joined Fast hero raises the unit's
            // Rush), so planning at them submits moves the movement resolver's per-model re-check
            // rejects - and that unit silently plays the solo resolver for the rest of the game.
            Dictionary<ModelID, (float Advance, float Rush, float Charge)> perModelBudgets =
                MovementRuleQueries.PerModelMoveBudgets(self, evaluator);
            float advance = perModelBudgets.Count == 0
                ? TacticalAnalysis.AdvanceDistance(self, evaluator)
                : perModelBudgets.Values.Min(b => b.Advance);
            float rush = perModelBudgets.Count == 0
                ? TacticalAnalysis.RushDistance(self, evaluator)
                : perModelBudgets.Values.Min(b => b.Rush);
            PlanBudget advanceBudget = new PlanBudget(advance,
                PerModelCaps(perModelBudgets, EActionType.Advance, advance));
            PlanBudget rushBudget = new PlanBudget(rush,
                PerModelCaps(perModelBudgets, EActionType.Rush, rush));

            bool canMoveThroughEnemies = MovementRuleQueries.CanMoveThroughEnemies(self, evaluator);
            bool ignoresDifficult = MovementRuleQueries.IgnoresDifficultTerrain(self, evaluator);
            bool ignoresAllTerrain = MovementRuleQueries.IgnoresAllTerrain(self, evaluator);

            Position start = Centroid(living, self);
            List<IUnit> enemies = LivingEnemies(tableState, self.PlayerID);
            List<IUnit> friends = LivingFriends(tableState, self);

            var terrain = tableState.Terrain.Objects.ToList();
            var enemyFootprints = MovementPlanner.LiveEnemyFootprints(tableState, self.PlayerID);
            float leadRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);
            // One terrain grid for the whole enumeration (built only if some candidate needs it).
            TerrainGrid? cachedGrid = null;
            Func<TerrainGrid> sharedGrid = () => cachedGrid ??= TerrainGrid.Build(terrain, leadRadius);

            // M2/M3 - objectives, both budgets (rush reaches farther; ranking keeps the useful one).
            foreach (IObjective objective in tableState.Objectives.Objects)
            {
                candidates.Add(Plan(EMacroIntent.AdvanceOnObjective,
                    $"intent=AdvanceOnObjective obj=({objective.Position.x:F0},{objective.Position.z:F0})",
                    EActionType.Advance, unit, living, tableState, evaluator, objective.Position, advanceBudget,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain,
                    goalRadius: TacticalAnalysis.ObjectiveSeizureRadiusInches,
                    targetObjective: objective, sharedGrid: sharedGrid));
                candidates.Add(Plan(EMacroIntent.RushObjective,
                    $"intent=RushObjective obj=({objective.Position.x:F0},{objective.Position.z:F0})",
                    EActionType.Rush, unit, living, tableState, evaluator, objective.Position, rushBudget,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain,
                    goalRadius: TacticalAnalysis.ObjectiveSeizureRadiusInches,
                    targetObjective: objective, sharedGrid: sharedGrid));
            }

            List<IUnit> rankedEnemies = enemies
                .OrderByDescending(TacticalAnalysis.UnitValue).Take(TopEnemies).ToList();
            bool hasRanged = self.GetRangedWeapons().Count > 0;
            bool hasMelee = self.GetMeleeWeapons().Count > 0;

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
                            EActionType.Advance, unit, living, tableState, evaluator, goal, advanceBudget,
                            canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain,
                            goalRadius: BandMarginInches, targetEnemy: enemy, band: band, sharedGrid: sharedGrid));
                    }
                }

                // M5 - charge to contact (generated even when the exchange looks bad - diversity rule).
                if (hasMelee)
                {
                    MacroAction charge = BuildCharge(unit, living, tableState, evaluator, self, enemy,
                        start, leadRadius, terrain, enemyFootprints,
                        canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, perModelBudgets);
                    if (charge.Feasibility == EFeasibility.Reachable)
                    {
                        candidates.Add(charge);
                    }
                    else
                    {
                        // Out of charge reach: a charge-budget move is not playable this activation
                        // (the declared action would be Move, whose budget is the rush distance), so
                        // approach instead - rush to a point outside the 1"-standoff rule on the lane
                        // to the nearest enemy model. Same M5 intent, played as a move; the planner's
                        // approach term makes this worth taking (#191 A4 gate fix).
                        IModel? nearest = NearestLivingModel(enemy, start);
                        Position aim = nearest?.Position ?? enemyPos;
                        float standoff = leadRadius + (nearest?.BaseRadiusInches ?? 0.5f)
                            + ApproachStandoffGapInches;
                        candidates.Add(Plan(EMacroIntent.ChargeToContact,
                            $"intent=ChargeToContact target={enemy.Name} approach",
                            EActionType.Rush, unit, living, tableState, evaluator,
                            PointAtDistanceFrom(aim, start, standoff), rushBudget,
                            canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain,
                            goalRadius: 1f, targetEnemy: enemy, sharedGrid: sharedGrid));
                    }
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
                    unit, living, tableState, evaluator, ClampToTable(away), rushBudget,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 1f,
                    targetEnemy: threat, sharedGrid: sharedGrid));
            }

            // M7 - seek cover from the biggest threat, behind the nearest cover piece in reach.
            if (enemies.Count > 0 && TryFindCoverGoal(tableState, start, Centroid(rankedEnemies[0]),
                    rush, out Position coverGoal))
            {
                candidates.Add(Plan(EMacroIntent.SeekCoverFrom,
                    $"intent=SeekCoverFrom from={rankedEnemies[0].Name}", EActionType.Rush,
                    unit, living, tableState, evaluator, coverGoal, rushBudget,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 1f,
                    targetEnemy: rankedEnemies[0], sharedGrid: sharedGrid));
            }

            // M8 - block the biggest threat's lane to our most valuable assets (a LINE across it).
            // A5-8: two lanes (top-2 assets by value) - the PLANNER's screen credit decides which
            // ward is genuinely threatened (threatened-value re-key); the generator just makes
            // sure the paying lane has a candidate standing on it.
            if (enemies.Count > 0)
            {
                IUnit threat = rankedEnemies[0];
                foreach (Position asset in AssetPositions(tableState, self, friends))
                {
                    Position threatPos2 = Centroid(threat);
                    Position lane = Midpoint(threatPos2, asset);
                    // The barrier spreads PERPENDICULAR to the lane it walls, whatever direction the
                    // blocker approaches from.
                    float laneDx = asset.x - threatPos2.x, laneDz = asset.z - threatPos2.z;
                    candidates.Add(Plan(EMacroIntent.Block,
                        $"intent=Block enemy={threat.Name}", EActionType.Rush,
                        unit, living, tableState, evaluator, lane, rushBudget,
                        canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 1f,
                        targetEnemy: threat, formation: MovementPlanner.EFormation.Line,
                        lineAxis: (-laneDz, laneDx), sharedGrid: sharedGrid));
                }
            }

            // M9 - escort valuable OTHER friendly units, interposing toward each one's threat
            // (top-2 by value, same A5-8 rationale as M8: the planner picks the paying lane).
            if (friends.Count > 0 && enemies.Count > 0)
            {
                foreach (IUnit ward in friends.OrderByDescending(TacticalAnalysis.UnitValue).Take(2))
                {
                    Position wardPos = Centroid(ward);
                    IUnit wardThreat = enemies.OrderBy(e => Distance(Centroid(e), wardPos)).First();
                    Position goal = PointAtDistanceFrom(wardPos, Centroid(wardThreat), -2.5f);
                    candidates.Add(Plan(EMacroIntent.Escort,
                        $"intent=Escort ally={ward.Name}", EActionType.Rush,
                        unit, living, tableState, evaluator, ClampToTable(goal), rushBudget,
                        canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 2f,
                        targetAlly: ward, sharedGrid: sharedGrid));
                }
            }

            // M10 - concentrate on the rest of the army's centroid.
            if (friends.Count > 0)
            {
                Position mass = MeanPosition(friends.Select(Centroid).ToList());
                candidates.Add(Plan(EMacroIntent.Concentrate,
                    "intent=Concentrate", EActionType.Rush,
                    unit, living, tableState, evaluator, mass, rushBudget,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 3f, sharedGrid: sharedGrid));
            }

            // M11 - move into cast range of the best spell's intended target. Only for units holding
            // spell tokens; LoS is not modeled here (recorded gap - the intent is a set-up move, and
            // A5 verifies whether Cast even permits same-activation movement). Advance, so casting
            // machinery still applies afterward if the engine allows it.
            AddMoveToCast(candidates, unit, living, tableState, evaluator, self, start,
                enemies, friends, advanceBudget, canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain);

            // M12 - a loaded transport routes toward where its CARGO wants to be (v1 proxy: the
            // nearest objective we do not already own outright - the cargo's most common plan).
            if (TransportUtilities.IsTransport(self)
                && TransportUtilities.GetOccupants(self, tableState.Units.Objects.ToList()).Any())
            {
                IObjective? destination = tableState.Objectives.Objects
                    .OrderBy(o => Distance(o.Position, start))
                    .FirstOrDefault(o => !o.OwnerID.HasValue || o.OwnerID.Value != self.PlayerID)
                    ?? tableState.Objectives.Objects.OrderBy(o => Distance(o.Position, start)).FirstOrDefault();
                if (destination != null)
                {
                    candidates.Add(Plan(EMacroIntent.DeliverCargo,
                        $"intent=DeliverCargo obj=({destination.Position.x:F0},{destination.Position.z:F0})",
                        EActionType.Rush, unit, living, tableState, evaluator, destination.Position, rushBudget,
                        canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain,
                        goalRadius: TacticalAnalysis.ObjectiveSeizureRadiusInches + 3f,
                        targetObjective: destination));
                }
            }

            return PruneWithDiversity(candidates, candidateBudget);
        }

        // M11: for each affordable spell, the intended target is the highest-value legal-affinity
        // unit; the goal sits just inside the spell's range of it. Friendly-affinity spells whose
        // best target is already in range produce no move (nothing to set up).
        private static void AddMoveToCast(List<MacroAction> candidates, DataBinding<UnitData> unit,
            List<DataBinding<ModelData>> living, ITableState tableState, RuleEvaluator evaluator,
            UnitData self, Position start, List<IUnit> enemies, List<IUnit> friends, PlanBudget advanceBudget,
            bool canMoveThroughEnemies, bool ignoresDifficult, bool ignoresAllTerrain)
        {
            int tokens = self.Tokens.GetTokenCount(TokenType.SpellTokens);
            if (tokens <= 0) return;

            ArmyData? army = null;
            foreach (IArmy candidateArmy in tableState.Armies.Objects)
            {
                if (candidateArmy.PlayerID == self.PlayerID && candidateArmy is ArmyData data)
                {
                    army = data;
                    break;
                }
            }
            if (army == null || army.Spells.Count == 0) return;

            foreach (RuntimeSpell spell in army.Spells)
            {
                if (spell.Threshold > tokens) continue;

                if (spell.Target.TargetAffinity == ETargetAffinity.Self) continue; // no positioning half
                bool wantsEnemy = spell.Target.TargetAffinity == ETargetAffinity.Foe;
                IUnit? target = (wantsEnemy ? enemies : friends)
                    .OrderByDescending(TacticalAnalysis.UnitValue).FirstOrDefault();
                if (target == null) continue;

                Position targetPos = Centroid(target);
                float castRange = Math.Max(1f, spell.Target.RangeInches - BandMarginInches);
                if (Distance(start, targetPos) <= castRange) continue; // already in range: no set-up move

                Position goal = PointAtDistanceFrom(targetPos, start, castRange);
                candidates.Add(Plan(EMacroIntent.MoveToCast,
                    $"intent=MoveToCast spell={spell.Name} target={target.Name}",
                    EActionType.Advance, unit, living, tableState, evaluator, goal, advanceBudget,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, goalRadius: 1f,
                    targetEnemy: wantsEnemy ? target : null,
                    targetAlly: wantsEnemy ? null : target));
                break; // one set-up candidate per activation keeps the family's budget share sane
            }
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
            bool canMoveThroughEnemies, bool ignoresDifficult, bool ignoresAllTerrain,
            IReadOnlyDictionary<ModelID, (float Advance, float Rush, float Charge)> perModelBudgets)
        {
            float fullChargeReach = TacticalAnalysis.ChargeDistanceAgainst(self, enemy, evaluator);
            // #264 issue 7: the same per-model split as the move families. The target-conditioned
            // shrink (Melee Shrouding) is unit-wide, so it comes off every model's own charge - the
            // composition DefinePathStage uses to build the request's per-model budgets.
            float chargeShrink = TacticalAnalysis.ChargeBudget(self, evaluator) - fullChargeReach;
            Func<ModelMoveEntry, ModelMoveBudget> chargeBudgetFor = entry =>
            {
                float cap = fullChargeReach;
                if (perModelBudgets.TryGetValue(entry.Model.GetValue().ID,
                        out (float Advance, float Rush, float Charge) budget))
                    cap = Math.Max(0f, budget.Charge - chargeShrink);
                return new ModelMoveBudget(cap, cap);
            };
            // Plan at the slowest model's reach so the whole unit can make the charge.
            float plannedReach = fullChargeReach;
            foreach (DataBinding<ModelData> model in living)
            {
                if (perModelBudgets.TryGetValue(model.GetValue().ID,
                        out (float Advance, float Rush, float Charge) budget))
                    plannedReach = Math.Min(plannedReach, Math.Max(0f, budget.Charge - chargeShrink));
            }
            float chargeReach = Math.Max(0f, plannedReach - 0.001f);
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
                // #216: friendly footprints included - a friendly-blind charge lane plans a move
                // the #205 stacking check rejects at resolve time, silently degrading the whole
                // activation to the solo resolver. With them, the backoff ladder shortens the
                // charge around the friendly instead (feasibility grades by the achieved gap).
                move = MovementPlanner.ValidateWithBackoff(
                    s => MovementPlanner.BuildCandidate(unit, living, start.x, start.z, ndx, ndz, s, chargeReach),
                    step, unit, living, chargeBudgetFor,
                    enemyFootprints, canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, terrain,
                    MovementPlanner.LiveFriendlyFootprints(tableState, self.PlayerID, self.ID),
                    // #256 S2: side-step around a friendly in the charge lane instead of halving to a stall.
                    (s, lat) => MovementPlanner.BuildCandidate(unit, living, start.x, start.z, ndx, ndz, s,
                        chargeReach, lateralOffsetInches: lat));
            }
            else
            {
                Position contactGoal = PointAtDistanceFrom(enemyPos, start,
                    contactDistance + MovementPlanner.ChargeContactTargetGapInches);
                move = MovementPlanner.PlanMoveToward(unit, living, tableState, contactGoal,
                    chargeReach, chargeReach, chargeBudgetFor,
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
            RuleEvaluator evaluator, Position goal, PlanBudget budget,
            bool canMoveThroughEnemies, bool ignoresDifficult, bool ignoresAllTerrain,
            float goalRadius, IUnit? targetEnemy = null, IObjective? targetObjective = null,
            IUnit? targetAlly = null, ERangeBand? band = null,
            MovementPlanner.EFormation formation = MovementPlanner.EFormation.Grid,
            (float X, float Z)? lineAxis = null, Func<TerrainGrid>? sharedGrid = null)
        {
            // The MOVE takes the float-precision margin; the VALIDATOR keeps the full budget
            // (the ResolverGuide gotcha - giving both the same reduced number makes the first
            // candidate fail its own budget check and the ladder halve a legal move).
            float safeBudget = Math.Max(0f, budget.Inches - 0.001f);
            (List<ModelMoveEntry> move, List<Position> route) = MovementPlanner.PlanMoveAlongRoute(
                unit, living, tableState, goal,
                safeBudget, safeBudget, budget.PerModel,
                canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain, formation, lineAxis, sharedGrid);

            Position end = MoveCentroid(move, living);
            // #264 issue 1: progress along the ROUTE, not the straight line. A detour around a large
            // impassible piece closes ~zero straight-line gap - sometimes a negative one - so a
            // correct move was graded Blocked and lost its family's pruning slot to a worse one.
            var terrain = tableState.Terrain.Objects.ToList();
            float baseRadius = living.Count == 0 ? 0.5f : living.Max(mb => mb.GetValue().BaseRadiusInches);
            float progress = RouteMetrics.Length(route)
                - RouteMetrics.RemainingFrom(route, end, terrain, baseRadius);

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

        private static IEnumerable<Position> AssetPositions(ITableState tableState, UnitData self,
            List<IUnit> friends)
        {
            if (friends.Count > 0)
            {
                foreach (IUnit friend in friends.OrderByDescending(TacticalAnalysis.UnitValue).Take(2))
                    yield return Centroid(friend);
                yield break;
            }

            IObjective? owned = tableState.Objectives.Objects
                .FirstOrDefault(o => o.OwnerID.HasValue && o.OwnerID.Value == self.PlayerID);
            if (owned != null) yield return owned.Position;
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
