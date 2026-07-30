using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 Instinctive slice 2 - the move-to-attack half of the compelled-attack primitive: "if it is
    /// able to shoot/charge an enemy unit, then it must immediately attack" extends to a unit that could
    /// make an attack POSSIBLE by moving (owner ruling 2026-07-30). This planner answers "does such a
    /// move exist?" - and answers it with a CONCRETE, fully validated move, never a bare boolean, because
    /// the #200 lesson is absolute: the menu may only compel what the stages can actually complete. A
    /// found move doubles as the auto-resolve option ChooseActionStage offers, so the player facing a
    /// fiddly "somewhere in range" search can take the detected move with one click.
    ///
    /// <para>Candidates are whole-unit re-packs (<see cref="CohesiveFormation.PackGrid"/>, the same
    /// builder the CLI auto-advance uses) stepped along the straight line toward each enemy, nearest
    /// first, validated with the same <see cref="MovementUtilities.ValidatePaths"/> call the movement
    /// resolvers make. The post-move attack test does not re-derive geometry: it APPLIES the candidate
    /// positions, asks the real gates (<see cref="ChooseRangedAttackStage.HasAnyFireableTarget"/> /
    /// <see cref="MeleeRangeUtilities.AreUnitsInMeleeRange"/>), and restores - so the detector and the
    /// post-move menu literally cannot disagree. The probe is synchronous (no awaits between apply and
    /// restore), so nothing on the engine thread ever observes the transient positions.</para>
    ///
    /// <para>Deliberately conservative: straight-line candidates only. A move that would need to hook
    /// around terrain is not found, and the unit simply is not compelled - the rule under-fires rather
    /// than ever compelling the impossible (fail-safe by construction).</para>
    /// </summary>
    public static class CompelledAttackMovePlanner
    {
        /// <summary>Post-move gap to the enemy's base for a charge-enabling move: inside the 2" melee
        /// cylinder with margin, outside the base-overlap the end-position validator rejects. The CLI
        /// auto-advance's standoff+0.05 constant, shared reasoning.</summary>
        private const float CHARGE_CONTACT_GAP_INCHES = GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES + 0.05f;

        /// <summary>Range margin for a shoot-enabling move, mirroring the AutoAdvance float-precision
        /// gotcha: end far enough INSIDE range that rounding cannot disqualify the follow-up shot.</summary>
        private const float SHOOT_RANGE_MARGIN_INCHES = 0.25f;

        /// <summary>The moves that would let the unit attack: either or both may be null when no valid
        /// enabling move was found for that attack kind.</summary>
        public readonly record struct EnablingMoves(List<ModelMoveEntry>? ShootMove, List<ModelMoveEntry>? ChargeMove);

        /// <summary>
        /// Searches for a shoot-enabling move (within the ADVANCE budget - a rush forfeits the shot) and a
        /// charge-enabling move (within the charge/rush hard cap) for <paramref name="unit"/>. Only worth
        /// calling when the unit cannot attack from where it stands; cheap when no enemy is plausibly
        /// reachable (the straight-line prefilter skips them all).
        /// </summary>
        public static EnablingMoves FindEnablingMoves(IGameContext gameContext, DataBinding<UnitData> unit)
        {
            UnitData unitValue = unit.GetValue();
            List<DataBinding<ModelData>> living = unitValue.ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return new EnablingMoves(null, null);

            // The same budget source DefinePathStage reads, so the candidate caps agree with what the
            // movement stage would enforce.
            MovementActionContext budgets = new MovementActionContext(gameContext, unit);
            if (budgets.MaxAdvanceDistance <= 0f) return new EnablingMoves(null, null);

            List<EnemyModelFootprint> enemyFootprints = MovementUtilities.GetEnemyModelFootprints(unit, gameContext);
            List<EnemyModelFootprint> friendlyFootprints = MovementUtilities.GetFriendlyModelFootprints(unit, gameContext);
            bool canMoveThroughEnemies = MovementRuleQueries.CanMoveThroughEnemies(unitValue, gameContext.RuleEvaluator);
            bool ignoresDifficult = MovementRuleQueries.IgnoresDifficultTerrain(unitValue, gameContext.RuleEvaluator);
            bool ignoresImpassible = MovementRuleQueries.IgnoresAllTerrain(unitValue, gameContext.RuleEvaluator);
            IReadOnlyList<ITerrain> terrain = gameContext.TableState.Terrain.Objects.ToList();

            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);
            float leadRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);

            bool hasRanged = unitValue.GetRangedWeapons().Count > 0;
            bool hasMelee = unitValue.GetMeleeWeapons().Count > 0;

            List<ModelMoveEntry>? shootMove = null;
            List<ModelMoveEntry>? chargeMove = null;

            foreach (DataBinding<UnitData> enemy in EnemiesNearestFirst(gameContext, unit, cx, cz))
            {
                UnitData enemyValue = enemy.GetValue();
                (float ex, float ez, float enemyRadius) = NearestLivingModel(enemyValue, cx, cz);
                float dx = ex - cx, dz = ez - cz;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist < 0.01f) continue;
                float ndx = dx / dist, ndz = dz / dist;
                float gapNow = dist - leadRadius - enemyRadius;

                // Charge-enabling: end the lead base inside the melee cylinder. Cap per model at
                // max(Rush, Charge shrunk against THIS enemy) - the same arithmetic DefinePathStage's
                // WorstCaseChargeDistance applies, but exact because the enemy is known here.
                if (chargeMove == null && hasMelee && !AircraftRules.IsAircraft(enemyValue))
                {
                    float shrink = budgets.MaxChargeDistance - MovementRuleQueries.EffectiveChargeDistanceAgainst(
                        unitValue, enemyValue, budgets.MaxChargeDistance, gameContext.RuleEvaluator);
                    float step = gapNow - CHARGE_CONTACT_GAP_INCHES;
                    float cap = MinBudget(budgets, living, m => Math.Max(m.rush, m.charge - shrink)) - 0.001f;
                    // The re-pack can move an outlier model farther than the centroid step; clamp like the
                    // CLI auto-advance so no model busts its budget. A clamp below the contact step means
                    // the candidate can't reach - the applied melee check below simply fails it.
                    step = CohesiveFormation.ClampRepackStep(living, cx, cz, step, cap);
                    if (step > 0f && step <= cap)
                    {
                        chargeMove = TryCandidate(living, cx + ndx * step, cz + ndz * step,
                            entry => PerModelCap(budgets, entry, m => Math.Max(m.rush, m.charge - shrink)),
                            enemyFootprints, friendlyFootprints, canMoveThroughEnemies, ignoresDifficult,
                            ignoresImpassible, terrain,
                            () => AnyEnemyInMeleeRange(gameContext, unit));
                    }
                }

                // Shoot-enabling: close to weapon range, capped at the ADVANCE budget. The needed step is
                // an estimate (max effective range vs this enemy); the applied-positions check is the
                // truth, so an estimate that lands short simply finds nothing.
                if (shootMove == null && hasRanged)
                {
                    float maxRange = MaxEffectiveRange(unitValue, enemyValue, gameContext.RuleEvaluator);
                    if (maxRange > 0f)
                    {
                        float step = Math.Max(0.1f, dist - maxRange + SHOOT_RANGE_MARGIN_INCHES);
                        float cap = MinBudget(budgets, living, m => m.advance) - 0.001f;
                        step = CohesiveFormation.ClampRepackStep(living, cx, cz, step, cap);
                        if (step <= cap)
                        {
                            shootMove = TryCandidate(living, cx + ndx * step, cz + ndz * step,
                                entry => PerModelCap(budgets, entry, m => m.advance),
                                enemyFootprints, friendlyFootprints, canMoveThroughEnemies, ignoresDifficult,
                                ignoresImpassible, terrain,
                                () => ChooseRangedAttackStage.HasAnyFireableTarget(unit, gameContext));
                        }
                    }
                }

                if (shootMove != null && chargeMove != null) break;
            }

            return new EnablingMoves(shootMove, chargeMove);
        }

        /// <summary>
        /// Whether <paramref name="entries"/> would leave the unit able to attack: in melee range of some
        /// enemy, or (when the move fits the advance allowance) with some fireable target. The shared
        /// predicate the movement resolvers enforce a compelled manual move against (slice 3), asked of
        /// the SAME gates the post-move menu will ask - the resolvers and the menu cannot disagree.
        /// </summary>
        public static bool WouldEndAbleToAttack(IGameContext gameContext, DataBinding<UnitData> unit,
            IReadOnlyList<ModelMoveEntry> entries, float maxAdvanceDistance)
        {
            List<ModelMoveEntry> entryList = entries as List<ModelMoveEntry> ?? entries.ToList();
            return WithProjectedPositions(entryList, () =>
            {
                if (AnyEnemyInMeleeRange(gameContext, unit)) return true;

                float moved = MovementUtilities.GetMaxMoveDistance(entryList);
                if (moved.LessThanOrAlmostEqual(maxAdvanceDistance)
                    && ChooseRangedAttackStage.HasAnyFireableTarget(unit, gameContext))
                {
                    return true;
                }
                return false;
            });
        }

        // ── Candidate machinery ──────────────────────────────────────────────────────────────────────────

        private static List<ModelMoveEntry>? TryCandidate(List<DataBinding<ModelData>> living,
            float centerX, float centerZ, Func<ModelMoveEntry, ModelMoveBudget> budgetFor,
            List<EnemyModelFootprint> enemyFootprints, List<EnemyModelFootprint> friendlyFootprints,
            bool canMoveThroughEnemies, bool ignoresDifficult, bool ignoresImpassible,
            IReadOnlyList<ITerrain> terrain, Func<bool> ableToAttackWhenApplied)
        {
            List<ModelMoveEntry> candidate = CohesiveFormation.PackGrid(living, centerX, centerZ);

            if (!MovementUtilities.ValidatePaths(candidate, budgetFor, enemyFootprints,
                    canMoveThroughEnemies, ignoresDifficult, ignoresImpassible, terrain, out _,
                    friendlyFootprints, lenientCoherency: true))
            {
                return null;
            }

            return WithProjectedPositions(candidate, ableToAttackWhenApplied) ? candidate : null;
        }

        /// <summary>
        /// Runs <paramref name="check"/> with every entry's model AT its candidate destination, then
        /// restores the real positions. Synchronous end to end - no awaits, so no other engine-thread work
        /// can observe the probe. Asking the real gates against applied positions is what makes the
        /// detector authoritative: a projected-geometry copy of the fireability check would drift.
        /// </summary>
        private static bool WithProjectedPositions(IReadOnlyList<ModelMoveEntry> entries, Func<bool> check)
        {
            var saved = new List<(ModelData Model, Position At)>(entries.Count);
            foreach (ModelMoveEntry entry in entries)
            {
                ModelData model = entry.Model.GetValue();
                saved.Add((model, model.Position));
                model.SetPosition(entry.Positions[^1]);
            }
            try
            {
                return check();
            }
            finally
            {
                foreach ((ModelData model, Position at) in saved)
                {
                    model.SetPosition(at);
                }
            }
        }

        // ── Queries ──────────────────────────────────────────────────────────────────────────────────────

        // Mirrors ChooseActionStage.GetCanCharge's screening: non-allied, on the battlefield, not an
        // Aircraft - so "the auto move would enable a charge" and "the menu offers Charge" agree.
        private static bool AnyEnemyInMeleeRange(IGameContext gameContext, DataBinding<UnitData> unit)
        {
            UnitData unitValue = unit.GetValue();
            IReadOnlyList<PlayerID> allied = AlliedPlayers(gameContext, unitValue.PlayerID);
            return gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !allied.Contains(a.PlayerID))
                .SelectMany(a => a.UnitBindings)
                .Where(e => !AircraftRules.IsAircraft(e.GetValue()))
                .Any(e => MeleeRangeUtilities.AreUnitsInMeleeRange(unitValue, e.GetValue()));
        }

        private static IEnumerable<DataBinding<UnitData>> EnemiesNearestFirst(IGameContext gameContext,
            DataBinding<UnitData> unit, float cx, float cz)
        {
            IReadOnlyList<PlayerID> allied = AlliedPlayers(gameContext, unit.GetValue().PlayerID);
            return gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !allied.Contains(a.PlayerID))
                .SelectMany(a => a.UnitBindings)
                .Where(e => e.GetValue().GetIsOnBattlefield()
                            && e.GetValue().Models.Any(m => m.GetIsAlive()))
                .OrderBy(e =>
                {
                    (float ex, float ez, _) = NearestLivingModel(e.GetValue(), cx, cz);
                    return (ex - cx) * (ex - cx) + (ez - cz) * (ez - cz);
                });
        }

        private static IReadOnlyList<PlayerID> AlliedPlayers(IGameContext gameContext, PlayerID player)
        {
            TeamData? team = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(player));
            return team?.Players ?? new List<PlayerID> { player };
        }

        private static (float x, float z, float radius) NearestLivingModel(UnitData enemy, float cx, float cz)
        {
            float bestX = 0f, bestZ = 0f, bestRadius = 0f, best = float.MaxValue;
            foreach (IModel model in enemy.Models)
            {
                if (!model.GetIsAlive()) continue;
                float dx = model.Position.x - cx, dz = model.Position.z - cz;
                float d = dx * dx + dz * dz;
                if (d < best)
                {
                    best = d;
                    bestX = model.Position.x;
                    bestZ = model.Position.z;
                    bestRadius = model.BaseRadiusInches;
                }
            }
            return (bestX, bestZ, bestRadius);
        }

        private static float MaxEffectiveRange(UnitData unit, UnitData enemy, RuleEvaluator evaluator)
        {
            float max = 0f;
            foreach (Weapon weapon in unit.GetRangedWeapons())
            {
                float range = RangeRuleQueries.EffectiveRange(unit, weapon, enemy, evaluator);
                if (range > max) max = range;
            }
            return max;
        }

        private static float MinBudget(MovementActionContext budgets, List<DataBinding<ModelData>> living,
            Func<(float advance, float rush, float charge), float> pick)
        {
            float min = float.MaxValue;
            foreach (DataBinding<ModelData> model in living)
            {
                budgets.TryGetModelMoveBudget(model.GetValue(), out float advance, out float rush, out float charge);
                float value = pick((advance, rush, charge));
                if (value < min) min = value;
            }
            return min == float.MaxValue ? 0f : min;
        }

        private static ModelMoveBudget PerModelCap(MovementActionContext budgets, ModelMoveEntry entry,
            Func<(float advance, float rush, float charge), float> pick)
        {
            budgets.TryGetModelMoveBudget(entry.Model.GetValue(), out float advance, out float rush, out float charge);
            float cap = pick((advance, rush, charge));
            return new ModelMoveBudget(cap, cap);
        }
    }
}
