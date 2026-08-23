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

        /// <summary>
        /// #361: nearest enemies unioned into the targeted families regardless of value. Value-only
        /// pruning has no eyes for reachability - the Hive Lord save's cheap APC 9.3" away (the only
        /// enemy with an in-reach charge lane) was dropped from every targeted family while three
        /// high-value squads 20-35" away kept all the slots. Nearness is measured to the closest
        /// LIVING MODEL, not the centroid - reach is edge-to-edge, and a spread unit's centroid can
        /// sit half a formation farther than its nearest base.
        /// </summary>
        private const int NearestEnemies = 2;

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
            DataBinding<UnitData> unit, int candidateBudget = DefaultCandidateBudget,
            bool seeThroughFriendlyUnits = false)
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
            // #376 (Grounded Speed): the budgets must see the terrain the move resolver will, or a
            // terrain-boosted unit plans short (and the reverse split degrades it to the solo resolver).
            var terrain = tableState.Terrain.Objects.ToList();
            // #384: what the M4 clear-lane probe must see through. With the see-through-allies house
            // rule off (official rules), other friendly units' bases block the lane exactly as the
            // shoot stage will count them; terrain-only otherwise. Movement budgets, grids, and
            // charge math keep the plain terrain list - bases are not terrain.
            List<ITerrain> sightTerrain = seeThroughFriendlyUnits
                ? terrain
                : terrain.Concat(LineOfSightUtilities.BuildFriendlySightBlockers(tableState, self))
                    .ToList();
            Dictionary<ModelID, (float Advance, float Rush, float Charge)> perModelBudgets =
                MovementRuleQueries.PerModelMoveBudgets(self, evaluator, terrain);
            float advance = perModelBudgets.Count == 0
                ? TacticalAnalysis.AdvanceDistance(self, evaluator, terrain)
                : perModelBudgets.Values.Min(b => b.Advance);
            float rush = perModelBudgets.Count == 0
                ? TacticalAnalysis.RushDistance(self, evaluator, terrain)
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

            var enemyFootprints = MovementPlanner.LiveEnemyFootprints(tableState, self.PlayerID);
            float leadRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);
            // #361: terrain clearance is the CIRCUMSCRIBED radius (see TerrainClearanceRadius);
            // leadRadius (inscribed) stays for base-contact arithmetic, where the refine loop's
            // shape-aware gap measurement corrects any error.
            float clearanceRadius = MovementPlanner.TerrainClearanceRadius(living);
            // One terrain grid for the whole enumeration (built only if some candidate needs it).
            TerrainGrid? cachedGrid = null;
            // Strider: no difficult multiplier in the router, so the shared grid matches the score
            // gradient's view of this unit (TacticianPlanner.UnitRoute).
            Func<TerrainGrid> sharedGrid = () =>
                cachedGrid ??= TerrainGridCache.Get(tableState, terrain, clearanceRadius, ignoresDifficult);

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
            // #361: the targeted families aim at the top-value enemies PLUS the nearest few - the
            // union keeps enumeration O(small) while guaranteeing the enemy standing next to us is
            // always evaluated. Enumerated NEAREST-first: value picks WHO is targeted, never the
            // order - the in-family pruning rank is stable, so under a tight budget the reachable
            // charge must not lose its slot to a hopeless 35" target enumerated earlier.
            // rankedEnemies[0] (the value pick) still anchors M6/M7/M8.
            List<IUnit> byDistance = enemies
                .OrderBy(e => NearestLivingModel(e, start) is IModel m
                    ? Distance(m.Position, start) : float.MaxValue)
                .ToList();
            List<IUnit> targetEnemies = byDistance
                .Where((e, i) => i < NearestEnemies || rankedEnemies.Contains(e))
                .ToList();
            bool hasRanged = self.GetRangedWeapons().Count > 0;
            // #355: "can fight in melee", not "carries a melee weapon" - an impact-only unit's charge IS
            // its attack, so a charge macro-action must be generated for it too.
            bool hasMelee = ChargeContactRules.CanFightInMelee(self);

            foreach (IUnit enemy in targetEnemies)
            {
                Position enemyPos = Centroid(enemy);

                // M4 - range bands (shooters only; Advance so the unit can still shoot).
                if (hasRanged)
                {
                    float reach = TacticalAnalysis.MaxWeaponRange(self, enemy, evaluator);
                    foreach (ERangeBand band in BandsFor(self, enemy, reach, evaluator, out float[] distances))
                    {
                        float d = distances[(int)band];
                        Position goal = ClearLaneGoal(
                            PointAtDistanceFrom(enemyPos, start, d), enemyPos, sightTerrain);
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
                        start, leadRadius, clearanceRadius, terrain, enemyFootprints,
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
                        // #361: circumscribed on both sides - an inscribed standoff parks a rect
                        // base's front edge inside the 1" rule and the ladder halves the approach
                        // to a crawl.
                        float standoff = clearanceRadius
                            + (nearest?.BaseShape.CircumscribedRadiusInches ?? 0.5f)
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
            if (TransportUtilities.IsTransport(self, evaluator)
                && TransportUtilities.GetOccupants(self, tableState.Units.Objects.ToList()).Any())
            {
                IObjective? destination = tableState.Objectives.Objects
                    .OrderBy(o => Distance(o.Position, start))
                    .FirstOrDefault(o => !o.OwnerID.HasValue
                        || !TacticalAnalysis.AreAllied(tableState, self.PlayerID, o.OwnerID.Value))
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

            // M13 - side-step (#359): the unit is standing on the advance lane of a friendly that
            // has not activated yet, so offer perpendicular Advance-budget steps - the argmax then
            // weighs clearing the lane (the planner's MoveLaneBlock penalty on staying) against
            // whatever standing still is worth. Advance, not Rush, so the unit can still shoot
            // from the new spot; gated on actually blocking, so uncrowded games generate nothing.
            if (enemies.Count > 0)
            {
                List<LaneGeometry.AdvanceLane> lanes = LaneGeometry.Build(tableState, evaluator, self);
                if (LaneGeometry.BlockValue(lanes, start) > 0f)
                {
                    Position enemyMass = MeanPosition(enemies.Select(Centroid).ToList());
                    float toEnemyX = enemyMass.x - start.x, toEnemyZ = enemyMass.z - start.z;
                    float axisLength = MathF.Sqrt(toEnemyX * toEnemyX + toEnemyZ * toEnemyZ);
                    if (axisLength > 0.001f)
                    {
                        (float perpX, float perpZ) = (-toEnemyZ / axisLength, toEnemyX / axisLength);
                        foreach (float side in new[] { 1f, -1f })
                        {
                            var goal = ClampToTable(new Position(
                                start.x + side * perpX * advance, start.z + side * perpZ * advance));
                            // goalRadius 2: the goal is an arbitrary clear-of-the-lane point, not
                            // a marker - a formation that repacks within a base-width of it has
                            // arrived, and grading that BudgetClipped would cost the tie-break
                            // that lets a completed side-step beat standing still.
                            candidates.Add(Plan(EMacroIntent.SideStep,
                                $"intent=SideStep side={(side > 0 ? "left" : "right")}",
                                EActionType.Advance, unit, living, tableState, evaluator, goal,
                                advanceBudget, canMoveThroughEnemies, ignoresDifficult,
                                ignoresAllTerrain, goalRadius: 2f, sharedGrid: sharedGrid));
                        }
                    }
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
            // Priced against the full purse (own tokens + nearby friendly accumulators), matching what
            // ChooseActionStage will actually allow. Measured from where the unit stands now: moving to set
            // up the cast can carry it out of an accumulator's range, so the estimate can be optimistic -
            // acceptable for a candidate generator whose moves the planner scores and may discard anyway.
            int tokens = SpellPurse.Available(tableState, evaluator, self);
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
            UnitData self, IUnit enemy, Position start, float leadRadius, float clearanceRadius,
            List<ITerrain> terrain, List<EnemyModelFootprint> enemyFootprints,
            bool canMoveThroughEnemies, bool ignoresDifficult, bool ignoresAllTerrain,
            IReadOnlyDictionary<ModelID, (float Advance, float Rush, float Charge)> perModelBudgets)
        {
            float fullChargeReach = TacticalAnalysis.ChargeDistanceAgainst(self, enemy, evaluator, terrain);
            // #264 issue 7: the same per-model split as the move families. The target-conditioned
            // shrink (Melee Shrouding) is unit-wide, so it comes off every model's own charge - the
            // composition DefinePathStage uses to build the request's per-model budgets.
            float chargeShrink = TacticalAnalysis.ChargeBudget(self, evaluator, terrain) - fullChargeReach;
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
            // #361: clearanceRadius (circumscribed), matching what the swept-base validator will
            // accept - probing with the inscribed leadRadius sent wedged rect-based units down the
            // straight branch, where every backoff arc failed and the charge graded Blocked-at-zero.
            bool straightClear = ignoresAllTerrain || !terrain.Any(t =>
                t.TerrainType.HasFlag(ETerrainType.Impassible)
                && t.Shape.DoesPathIntersectZone(new Float2(start.x, start.z),
                    new Float2(enemyPos.x, enemyPos.z), clearanceRadius));
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
                // #361: the routed goal must clear the enemy for the WHOLE base at any approach
                // facing - placed with the inscribed contactDistance, a rect base's front edge lands
                // inside the target, the validator rejects every long arc, and the ladder stalls the
                // charge mid-route (the Hive Lord save's APC charge died at 3" of a 9" dogleg).
                // Circumscribed radii aim slightly short; NudgeToContact closes the remainder (and
                // the route's grid-quantization scatter) with a validated final step.
                float routedContact = clearanceRadius
                    + (nearestModel?.BaseShape.CircumscribedRadiusInches ?? 0.5f);
                Position contactGoal = PointAtDistanceFrom(enemyPos, start,
                    routedContact + MovementPlanner.ChargeContactTargetGapInches);
                move = MovementPlanner.PlanMoveToward(unit, living, tableState, contactGoal,
                    chargeReach, chargeReach, chargeBudgetFor,
                    canMoveThroughEnemies, ignoresDifficult, ignoresAllTerrain);
                move = MovementPlanner.NudgeToContact(move, unit, living, tableState, enemy,
                    chargeBudgetFor, enemyFootprints, canMoveThroughEnemies,
                    ignoresDifficult, ignoresAllTerrain, terrain);
            }

            // #361: the grade asks "did we reach the unit we are CHARGING?", so the gap is measured
            // against the TARGET's models only. Measured against all enemies, a charge that
            // dead-ended on a bystander in the lane graded Reachable, was declared as a real charge,
            // and the stage's #312 reach validation (base-to-base vs the declared target) then
            // rejected it at resolve time - the #216 silent-degradation class. The construction
            // above keeps the all-enemies lists: they are what make the move legal.
            float gap = MovementPlanner.MinEnemyGap(move, MovementPlanner.UnitFootprints(enemy));
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
            // #361: the same clearance radius the route was planned with (see TerrainClearanceRadius).
            float baseRadius = MovementPlanner.TerrainClearanceRadius(living);
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

        /// <summary>
        /// #363: a band endpoint whose sight line to the target is cut by Blocking terrain is a
        /// firing position in name only - the scorer now prices it at zero (phantom volleys), so
        /// without this the whole EngageAtRange family dies wherever a wall stands on the straight
        /// lane, even when a clear lane exists a short side-step away (the BattleBrothers corner:
        /// clear lane 5" from where the unit stood, structurally unfindable). Rotate the band point
        /// around the target in 15-degree steps (up to 90 each way, nearer-to-us side first per
        /// step, fixed order - bench determinism) and take the first sample that both stays on the
        /// table and sees the target. All samples keep the band distance; none found = keep the
        /// straight goal (previous behavior, and the scorer prices it truthfully now).
        /// </summary>
        private static Position ClearLaneGoal(Position straightGoal, Position enemyPos,
            IReadOnlyList<ITerrain> terrain)
        {
            if (LineOfSightUtilities.HasLineOfSight(straightGoal, enemyPos, terrain))
                return straightGoal;

            float dx = straightGoal.x - enemyPos.x, dz = straightGoal.z - enemyPos.z;
            for (int step = 1; step <= 6; step++)
            {
                float radians = step * (MathF.PI / 12f);
                float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
                Position plus = ClampToTable(new Position(
                    enemyPos.x + dx * cos - dz * sin, enemyPos.z + dx * sin + dz * cos));
                Position minus = ClampToTable(new Position(
                    enemyPos.x + dx * cos + dz * sin, enemyPos.z - dx * sin + dz * cos));

                // Same deviation both ways - try the sample nearer the straight goal first (the
                // cheaper walk); strict inequality keeps the tie deterministic (plus first).
                (Position first, Position second) = Distance(minus, straightGoal)
                    < Distance(plus, straightGoal) ? (minus, plus) : (plus, minus);
                if (LineOfSightUtilities.HasLineOfSight(first, enemyPos, terrain)) return first;
                if (LineOfSightUtilities.HasLineOfSight(second, enemyPos, terrain)) return second;
            }
            return straightGoal;
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

        // #296: team-aware sides - the targeted families (bands, charges, fallback, block) must not
        // aim at a 2v2 teammate, and escort/concentrate/block should treat the teammate's units as
        // the assets they are. AreAllied with no team == same player, so 1v1 is unchanged.
        private static List<IUnit> LivingEnemies(ITableState tableState, PlayerID self) =>
            tableState.Units.Objects
                .Where(u => !TacticalAnalysis.AreAllied(tableState, self, u.PlayerID)
                    && u.Models.Any(m => m.GetIsAlive()))
                .ToList();

        private static List<IUnit> LivingFriends(ITableState tableState, UnitData self) =>
            tableState.Units.Objects
                .Where(u => TacticalAnalysis.AreAllied(tableState, self.PlayerID, u.PlayerID)
                    && !ReferenceEquals(u, self)
                    && u.Models.Any(m => m.GetIsAlive()))
                .ToList();
    }
}
