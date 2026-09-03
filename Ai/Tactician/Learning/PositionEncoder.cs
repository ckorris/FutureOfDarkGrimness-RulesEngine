using FDG.Data;
using FDG.Rules.Dispatch;

namespace FDG.Ai.Tactician.Learning
{
    /// <summary>
    /// The C1 self-play feature vector (#191 campaign step 4, docs/tactician-c1-schema.md): a
    /// scale-free read of the board from one activation boundary, for the ACTING player. Every
    /// value is a fraction, a share, or normalized by a scale reference (schema sec 0 rule 1) so
    /// one net reads a 1k skirmish and a 4k battle the same way, and features are aggregated per
    /// SIDE (SELF/ALLY/ENEMY_SUM/ENEMY_MAX, schema sec 0 rule 2) so 1v1 and 2v2 share one width.
    /// <para>
    /// Pure function of the table state handed in - never rolls dice, never mutates anything
    /// (schema sec 7 check 5: byte-identical output for a fixed seed is a determinism requirement
    /// on the encoder itself). Budget is under 5ms/call (schema sec 0 rule 3); nothing here runs a
    /// CombatMath sweep per unit (the firepower-band idea is explicitly deferred to v2, schema
    /// sec 4) - only cheap TacticalAnalysis primitives and O(units) or O(units x objectives) scans.
    /// </para>
    /// <para>
    /// <c>activation_frac</c> and <c>acting_side_is_first</c> are round-scoped facts the encoder
    /// has no cheap way to derive from <see cref="ITableState"/> alone (the team activation order
    /// lives in engine-internal round state, not the table-state read surface) - callers that
    /// observe the real activation sequence (the FdgLab exporter) pass them in.
    /// </para>
    /// </summary>
    public static class PositionEncoder
    {
        public const int SchemaVersion = 1;
        public const int GlobalFeatureCount = 7;
        public const int PerSideFeatureCount = 15;
        public const int VectorWidth = GlobalFeatureCount + PerSideFeatureCount * 4; // 67

        // DEFAULT_TABLE_WIDTH/HEIGHT_INCHES (72x48): the schema's scale reference for on-table
        // distances. Not read off the actual table's terrain bounds - GameWideConstants is the
        // single normalization reference every game uses, deployment zones included.
        private static readonly float TableDiagonalInches = MathF.Sqrt(
            GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES
            + GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES * GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

        /// <summary>
        /// Encodes the 67-float v1 vector for <paramref name="actingPlayer"/>'s activation boundary.
        /// </summary>
        /// <param name="boundaryIndexInRound">0-based index of this activation within the current round.</param>
        /// <param name="expectedBoundariesThisRound">The round's total living-unit count at round
        /// start (the caller's estimate of how many activations the round will have); at least 1.</param>
        /// <param name="actingSideIsFirst">Whether the acting player's SIDE took the first
        /// activation of this round (observed activation order - see the type doc).</param>
        /// <param name="totalGamePoints">Sum of every side's army points for this game, used only
        /// for <c>points_norm</c> (schema sec 2's deliberate absolute-value exception).</param>
        public static float[] Encode(ITableState tableState, RuleEvaluator evaluator, PlayerID actingPlayer,
            int boundaryIndexInRound, int expectedBoundariesThisRound, bool actingSideIsFirst,
            float totalGamePoints)
        {
            var v = new float[VectorWidth];

            List<PlayerID> allPlayers = tableState.Armies.Objects.Select(a => a.PlayerID).Distinct().ToList();
            List<PlayerID> allies = allPlayers.Where(p => !p.Equals(actingPlayer)
                && TacticalAnalysis.AreAllied(tableState, actingPlayer, p)).ToList();
            List<PlayerID> enemies = allPlayers.Where(p => !TacticalAnalysis.AreAllied(tableState, actingPlayer, p))
                .ToList();
            var friendly = new List<PlayerID> { actingPlayer };
            friendly.AddRange(allies);

            var terrain = TacticalAnalysis.TerrainOf(tableState);
            List<ObjectiveProjection> projections = TacticalAnalysis.ProjectObjectives(tableState);
            int objectiveCount = Math.Max(1, tableState.Objectives.Objects.Count());

            // Global denominators (schema sec 3): every *_share feature divides by the ALL-sides
            // total, computed once so the four blocks below only need their own numerator.
            var globals = new Globals(tableState);

            int round = tableState.Progress.RoundCount ?? 1;
            int totalRounds = Math.Max(1, tableState.Progress.TotalRounds);

            // --- 7 global scalars --------------------------------------------------------------
            v[0] = Math.Clamp((float)round / totalRounds, 0f, 1f); // round_frac
            v[1] = Math.Clamp((float)(totalRounds - round) / totalRounds, 0f, 1f); // rounds_left_frac
            v[2] = Math.Clamp(objectiveCount / 5f, 0f, 1f); // objective_count_norm
            v[3] = Math.Clamp(friendly.Count / 4f, 0f, 1f); // players_per_side_norm
            v[4] = Math.Clamp(totalGamePoints / 4000f, 0f, 1f); // points_norm (sec 2's exception)
            v[5] = Math.Clamp((float)boundaryIndexInRound / Math.Max(1, expectedBoundariesThisRound), 0f, 1f); // activation_frac
            v[6] = actingSideIsFirst ? 1f : 0f; // acting_side_is_first

            // --- 4 per-side blocks (15 floats each) ---------------------------------------------
            float[] self = ComputeBlock(tableState, evaluator, terrain, projections, objectiveCount,
                new List<PlayerID> { actingPlayer }, enemies, globals);
            float[] ally = allies.Count == 0
                ? new float[PerSideFeatureCount]
                : ComputeBlock(tableState, evaluator, terrain, projections, objectiveCount,
                    allies, enemies, globals);
            float[] enemySum = enemies.Count == 0
                ? new float[PerSideFeatureCount]
                : ComputeBlock(tableState, evaluator, terrain, projections, objectiveCount,
                    enemies, friendly, globals);
            float[] enemyMax = new float[PerSideFeatureCount];
            foreach (PlayerID enemy in enemies)
            {
                float[] block = ComputeBlock(tableState, evaluator, terrain, projections, objectiveCount,
                    new List<PlayerID> { enemy }, friendly, globals);
                for (int i = 0; i < PerSideFeatureCount; i++)
                    enemyMax[i] = Math.Max(enemyMax[i], block[i]);
            }

            Array.Copy(self, 0, v, GlobalFeatureCount, PerSideFeatureCount);
            Array.Copy(ally, 0, v, GlobalFeatureCount + PerSideFeatureCount, PerSideFeatureCount);
            Array.Copy(enemySum, 0, v, GlobalFeatureCount + PerSideFeatureCount * 2, PerSideFeatureCount);
            Array.Copy(enemyMax, 0, v, GlobalFeatureCount + PerSideFeatureCount * 3, PerSideFeatureCount);

            return v;
        }

        // Global (all-sides) totals shared by every block's *_share denominator (schema sec 3).
        private readonly struct Globals
        {
            public readonly float ValueTotal;
            public readonly float RangedTotal;
            public readonly float MeleeTotal;
            public readonly int LivingUnitsTotal;

            public Globals(ITableState tableState)
            {
                float value = 0f, ranged = 0f, melee = 0f;
                int living = 0;
                foreach (IUnit unit in LivingUnits(tableState, null))
                {
                    value += TacticalAnalysis.UnitValue(unit);
                    ranged += TacticalAnalysis.RangedOutputWounds(unit);
                    melee += TacticalAnalysis.MeleeOutputWounds(unit);
                    living++;
                }
                ValueTotal = value;
                RangedTotal = ranged;
                MeleeTotal = melee;
                LivingUnitsTotal = living;
            }
        }

        private static float[] ComputeBlock(ITableState tableState, RuleEvaluator evaluator,
            IReadOnlyList<ITerrain> terrain, List<ObjectiveProjection> projections, int objectiveCount,
            List<PlayerID> members, List<PlayerID> opposing, Globals globals)
        {
            var block = new float[PerSideFeatureCount];

            List<IUnit> livingUnits = LivingUnits(tableState, members).ToList();
            int rosterCount = RosterCount(tableState, members);

            float woundsCurrent = 0f, woundsMax = 0f, value = 0f, ranged = 0f, melee = 0f;
            int unactivated = 0, reserve = 0, seizers = 0;
            float objDistSum = 0f, objDistMin = float.MaxValue;
            foreach (IUnit unit in livingUnits)
            {
                woundsCurrent += unit.RemainingWounds;
                woundsMax += unit.MaxWounds;
                value += TacticalAnalysis.UnitValue(unit);
                ranged += TacticalAnalysis.RangedOutputWounds(unit);
                melee += TacticalAnalysis.MeleeOutputWounds(unit);
                if (!unit.Tokens.HasToken(Rules.Foundation.TokenType.ActivatedThisRound)) unactivated++;
                if (Rules.Dispatch.ReserveRules.IsInReserve(unit)) reserve++;
                if (TacticalAnalysis.CanSeizeObjectives(unit)) seizers++;

                float nearest = float.MaxValue;
                foreach (IObjective objective in tableState.Objectives.Objects)
                {
                    float d = TacticalAnalysis.MinBaseEdgeDistanceToPoint(unit, objective.Position);
                    if (d < nearest) nearest = d;
                }
                if (nearest < float.MaxValue)
                {
                    objDistSum += nearest;
                    objDistMin = Math.Min(objDistMin, nearest);
                }
            }

            int n = Math.Max(1, livingUnits.Count);
            block[0] = Frac(woundsCurrent, woundsMax); // health_frac
            block[1] = Frac(value, globals.ValueTotal); // value_share
            block[2] = Frac(livingUnits.Count, rosterCount); // units_alive_frac
            block[3] = Frac(ranged, globals.RangedTotal); // ranged_share
            block[4] = Frac(melee, globals.MeleeTotal); // melee_share
            block[5] = Frac(unactivated, livingUnits.Count); // activations_left_frac
            block[6] = Frac(CountHeldBy(projections, members), objectiveCount); // obj_held_share
            block[7] = Frac(CountContestedBy(projections, members), objectiveCount); // obj_contested_share
            block[8] = livingUnits.Count == 0 ? 0f
                : Math.Clamp(objDistSum / n / TableDiagonalInches, 0f, 1f); // mean_obj_dist_norm
            block[9] = objDistMin == float.MaxValue ? 1f
                : Math.Clamp(objDistMin / TableDiagonalInches, 0f, 1f); // min_obj_dist_norm

            // mobility_norm and threat_coverage's per-unit reach share one O(units) pass over
            // livingUnits - each unit's AdvanceDistance/ChargeBudget rule evaluation runs ONCE here,
            // not once per (threat x target) pair. threat_coverage then compares precomputed reach
            // against raw centroid distance (O(1) arithmetic per pair): the schema's 5ms budget
            // (sec 0 rule 3, sec 7 check 6) rules out a per-pair CombatMath-grade sweep, and a
            // pair's real threat range is target-conditioned (Melee Shrouding etc) anyway - this
            // trades that precision for the coarse yes/no coverage fraction the feature actually is.
            float mobilitySum = 0f;
            var reach = new float[livingUnits.Count];
            var centroids = new Position[livingUnits.Count];
            for (int u = 0; u < livingUnits.Count; u++)
            {
                IUnit unit = livingUnits[u];
                mobilitySum += TacticalAnalysis.AdvanceDistance(unit, evaluator, terrain);
                reach[u] = CheapThreatReach(unit, evaluator, terrain);
                centroids[u] = Centroid(unit);
            }
            block[10] = livingUnits.Count == 0 ? 0f
                : Math.Clamp(mobilitySum / n / GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, 0f, 1f); // mobility_norm

            List<IUnit> opposingLiving = LivingUnits(tableState, opposing).ToList();
            int covered = 0;
            foreach (IUnit target in opposingLiving)
            {
                bool inRange = false;
                for (int u = 0; u < livingUnits.Count; u++)
                {
                    if (reach[u] >= TacticalAnalysis.MinBaseEdgeDistanceToPoint(target, centroids[u]))
                    {
                        inRange = true;
                        break;
                    }
                }
                if (inRange) covered++;
            }
            block[11] = Frac(covered, opposingLiving.Count); // threat_coverage
            block[12] = Frac(reserve, livingUnits.Count); // reserve_frac
            block[13] = Frac(seizers, livingUnits.Count); // seizer_frac
            block[14] = Frac(livingUnits.Count, globals.LivingUnitsTotal); // activation_share

            return block;
        }

        public const int EntityFeatureCount = 16; // 13 scalars + 3-wide SELF/ALLY/ENEMY one-hot

        /// <summary>
        /// The per-unit entity table (schema sec 5), for the 5%-of-games sample only - the caller
        /// decides whether to call this, the encoder just answers when asked. One row per LIVING
        /// on-table unit of every player in the game, each row's 16 floats being: value share of
        /// its own side, health frac, alive (always 1 here - dead units are skipped), activated,
        /// in reserve, can seize, mobility norm, ranged share, melee share, normalized distance to
        /// nearest objective, normalized distance to nearest enemy, threat-coverage frac (this
        /// UNIT's own coverage of the opposing side, not its side's), is-caster, then a 3-wide
        /// SELF/ALLY/ENEMY one-hot relative to <paramref name="actingPlayer"/>.
        /// <para>
        /// is-caster is a crude proxy (any rule definition named "Caster") pending a real caster
        /// query surface - acceptable for a sampled, v2-only table that nothing in v1 trains on
        /// (schema sec 5's explicit rationale: log it now so v2 never needs a regeneration run).
        /// </para>
        /// </summary>
        public static List<float[]> EncodeEntities(ITableState tableState, RuleEvaluator evaluator,
            PlayerID actingPlayer)
        {
            var terrain = TacticalAnalysis.TerrainOf(tableState);
            List<PlayerID> allPlayers = tableState.Armies.Objects.Select(a => a.PlayerID).Distinct().ToList();
            List<PlayerID> allies = allPlayers.Where(p => !p.Equals(actingPlayer)
                && TacticalAnalysis.AreAllied(tableState, actingPlayer, p)).ToList();
            List<PlayerID> enemies = allPlayers.Where(p => !TacticalAnalysis.AreAllied(tableState, actingPlayer, p))
                .ToList();

            var globals = new Globals(tableState);
            var rows = new List<float[]>();

            foreach (PlayerID owner in allPlayers)
            {
                bool isSelf = owner.Equals(actingPlayer);
                bool isAlly = !isSelf && allies.Contains(owner);
                List<PlayerID> ownSide = isSelf || isAlly
                    ? new List<PlayerID> { actingPlayer }.Concat(allies).ToList()
                    : enemies;
                List<PlayerID> opposingSide = isSelf || isAlly ? enemies
                    : new List<PlayerID> { actingPlayer }.Concat(allies).ToList();
                List<IUnit> ownSideLiving = LivingUnits(tableState, ownSide).ToList();
                List<IUnit> opposingLiving = LivingUnits(tableState, opposingSide).ToList();
                float ownSideValue = ownSideLiving.Sum(TacticalAnalysis.UnitValue);

                foreach (IUnit unit in LivingUnits(tableState, new List<PlayerID> { owner }))
                {
                    var row = new float[EntityFeatureCount];
                    row[0] = Frac(TacticalAnalysis.UnitValue(unit), ownSideValue); // value share of own side
                    row[1] = Frac(unit.RemainingWounds, unit.MaxWounds); // health_frac
                    row[2] = 1f; // alive (dead units never reach this loop)
                    row[3] = unit.Tokens.HasToken(Rules.Foundation.TokenType.ActivatedThisRound) ? 1f : 0f; // activated
                    row[4] = Rules.Dispatch.ReserveRules.IsInReserve(unit) ? 1f : 0f; // in reserve
                    row[5] = TacticalAnalysis.CanSeizeObjectives(unit) ? 1f : 0f; // can seize
                    row[6] = Math.Clamp(TacticalAnalysis.AdvanceDistance(unit, evaluator, terrain)
                        / GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, 0f, 1f); // mobility_norm
                    row[7] = Frac(TacticalAnalysis.RangedOutputWounds(unit), globals.RangedTotal); // ranged_share
                    row[8] = Frac(TacticalAnalysis.MeleeOutputWounds(unit), globals.MeleeTotal); // melee_share

                    float nearestObj = float.MaxValue;
                    foreach (IObjective objective in tableState.Objectives.Objects)
                        nearestObj = Math.Min(nearestObj, TacticalAnalysis.MinBaseEdgeDistanceToPoint(unit, objective.Position));
                    row[9] = nearestObj == float.MaxValue ? 1f
                        : Math.Clamp(nearestObj / TableDiagonalInches, 0f, 1f); // dist to nearest objective

                    Position at = Centroid(unit);
                    float nearestEnemy = float.MaxValue;
                    foreach (IUnit enemy in opposingLiving)
                        nearestEnemy = Math.Min(nearestEnemy, Distance(at, Centroid(enemy)));
                    row[10] = nearestEnemy == float.MaxValue ? 1f
                        : Math.Clamp(nearestEnemy / TableDiagonalInches, 0f, 1f); // dist to nearest enemy

                    float unitReach = CheapThreatReach(unit, evaluator, terrain); // see the block encoder's note
                    int covered = opposingLiving.Count(target =>
                        unitReach >= TacticalAnalysis.MinBaseEdgeDistanceToPoint(target, at));
                    row[11] = Frac(covered, opposingLiving.Count); // threat_coverage
                    row[12] = unit.RuleDefinitions.Any(r =>
                        r.RequestedName.Contains("Caster", StringComparison.OrdinalIgnoreCase)) ? 1f : 0f; // is-caster

                    row[13] = isSelf ? 1f : 0f;
                    row[14] = isAlly ? 1f : 0f;
                    row[15] = !isSelf && !isAlly ? 1f : 0f;
                    rows.Add(row);
                }
            }

            return rows;
        }

        // A unit's own threatening reach, target-independent (unlike TacticalAnalysis.ThreatRangeAgainst,
        // which is per-target and does a rule evaluation per weapon per call - too expensive to run
        // O(units^2) times here). Raw weapon range, no RangeRuleQueries target-conditioning.
        private static float CheapThreatReach(IUnit unit, RuleEvaluator evaluator, IReadOnlyList<ITerrain> terrain)
        {
            float advance = TacticalAnalysis.AdvanceDistance(unit, evaluator, terrain);
            float maxWeaponRange = 0f;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                foreach (Weapon weapon in model.Weapons)
                    if (weapon.RangeInches > maxWeaponRange) maxWeaponRange = weapon.RangeInches;
            }
            float shooting = maxWeaponRange > 0f ? advance + maxWeaponRange : 0f;
            float melee = Rules.Dispatch.ChargeContactRules.CanFightInMelee(unit)
                ? TacticalAnalysis.ChargeBudget(unit, evaluator, terrain) + GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL
                : 0f;
            return Math.Max(shooting, melee);
        }

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        // A "living unit's own centroid" approximation of where it can be threatened FROM - the
        // schema's threat_coverage is a coarse fraction, not per-model geometry (that budget is
        // spent in TacticianPlanner's Score, not here).
        private static Position Centroid(IUnit unit)
        {
            float x = 0f, z = 0f;
            int count = 0;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                x += model.Position.x;
                z += model.Position.z;
                count++;
            }
            return count == 0 ? new Position(0, 0) : new Position(x / count, z / count);
        }

        private static float Frac(float numerator, float denominator) =>
            denominator <= 0f ? 0f : Math.Clamp(numerator / denominator, 0f, 1f);

        private static int CountHeldBy(List<ObjectiveProjection> projections, List<PlayerID> members) =>
            projections.Count(p => p.ProjectedOwner.HasValue && members.Contains(p.ProjectedOwner.Value));

        private static int CountContestedBy(List<ObjectiveProjection> projections, List<PlayerID> members) =>
            projections.Count(p => p.PlayersInRange.Any(members.Contains)
                && !(p.ProjectedOwner.HasValue && members.Contains(p.ProjectedOwner.Value)));

        // #296: FRIENDS/enemies are whole TEAMS - members is a list of PlayerIDs already resolved
        // to one side. null means "every player" (globals).
        private static IEnumerable<IUnit> LivingUnits(ITableState tableState, List<PlayerID>? members)
        {
            foreach (IArmy army in tableState.Armies.Objects)
            {
                if (members != null && !members.Contains(army.PlayerID)) continue;
                if (army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> binding in data.UnitBindings)
                {
                    UnitData unit = binding.GetValue();
                    if (unit.GetIsAlive() && unit.GetIsOnBattlefield())
                        yield return unit;
                }
            }
        }

        // The append-only roster size (schema sec 3: "UnitBindings is append-only, so the
        // denominator is the starting roster for free") - dead units still count.
        private static int RosterCount(ITableState tableState, List<PlayerID> members)
        {
            int count = 0;
            foreach (IArmy army in tableState.Armies.Objects)
            {
                if (!members.Contains(army.PlayerID)) continue;
                if (army is not ArmyData data) continue;
                count += data.UnitBindings.Count;
            }
            return count;
        }
    }
}
