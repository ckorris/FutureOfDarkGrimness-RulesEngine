using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Derives movement capabilities from a unit's #042 rules. Mirrors <see cref="SightRuleQueries"/>:
    /// a single, non-logging read of the rule dispatch that the movement validator and every move
    /// resolver (GUI/CLI/AI) share, so they agree on what a unit is allowed to do.
    /// </summary>
    public static class MovementRuleQueries
    {
        /// <summary>
        /// Whether <paramref name="unit"/> may move through enemy units (Strafing's fly-over, a future
        /// Flying rule). Drives the <c>canMoveThroughEnemies</c> flag on
        /// <see cref="Stages.MovementUtilities.ValidatePaths(System.Collections.Generic.List{ModelMoveEntry},float,System.Collections.Generic.IEnumerable{Stages.MovementUtilities.EnemyModelFootprint},bool,System.Collections.Generic.IEnumerable{ITerrain},out System.Collections.Generic.List{ReasonForInvalidMove})"/>:
        /// it skips the pass-through block (but the unit still may not END a move stacked on an enemy).
        /// Non-logging — safe to call per-frame while a resolver builds its preview.
        /// </summary>
        public static bool CanMoveThroughEnemies(IUnit unit, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new MoveThroughEnemyContext(unit), (unit, ERuleSeat.Actor)))
            {
                if (op is RuleOperation.IgnoreEnemyMovementBlock) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="unit"/> ignores the difficult-terrain movement cap (Strider; a future
        /// Flying rule reuses the same <see cref="RuleOperation.IgnoreTerrainEffects"/>). Drives the
        /// <c>ignoresDifficultTerrain</c> flag threaded into
        /// <see cref="Stages.MovementUtilities.ValidateMovingThroughDifficultTerrain"/>: when set, a move
        /// crossing Difficult terrain is no longer capped at <see cref="Utilities.GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES"/>.
        /// Non-logging — safe to call per-frame while a resolver builds its preview. Note this waives only the
        /// difficult-terrain cap; Dangerous-terrain tests and the enemy move-through block are unaffected (the
        /// "ignore all terrain" Flying facet is #029).
        /// </summary>
        public static bool IgnoresDifficultTerrain(IUnit unit, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new MoveThroughTerrainContext(unit), (unit, ERuleSeat.Actor)))
            {
                if (op is RuleOperation.IgnoreTerrainEffects) return true;
            }
            return false;
        }

        /// <summary>
        /// The effective charge distance <paramref name="charger"/> may move toward <paramref name="target"/>,
        /// given the charger's own (already movement-modified) <paramref name="baseChargeInches"/>: the base
        /// plus every <see cref="RuleOperation.ApplyMovementBonus"/> for the Charge action contributed at
        /// <see cref="EHookID.Movement_OnChargeDeclared"/> — most importantly the target's Subject-seat charge
        /// penalty (Melee Shrouding's −3"), but also any charger Actor-seat charge bonus seated at that hook —
        /// then floored by the largest <c>MinResultInches</c> among them (Melee Shrouding's "to a min. of 6\"").
        /// Equals <paramref name="baseChargeInches"/> when no such rule is in play. Fires the previously dormant
        /// <see cref="Contexts.ChargeDeclaredContext"/>. Non-logging — safe to call per-frame while building UI.
        /// Note this is the per-target value; the engine applies it conservatively (worst case among reachable
        /// enemies) because a charge's target isn't pinned until the move ends (see #029).
        /// </summary>
        public static float EffectiveChargeDistanceAgainst(IUnit charger, IUnit target, float baseChargeInches,
            RuleEvaluator evaluator)
        {
            float delta = 0f;
            float floor = 0f;
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new ChargeDeclaredContext(charger, target, baseChargeInches),
                         (charger, ERuleSeat.Actor), (target, ERuleSeat.Subject)))
            {
                if (op is RuleOperation.ApplyMovementBonus move && move.ActionType == EActionType.Charge)
                {
                    delta += move.DistanceInches;
                    if (move.MinResultInches > floor) floor = move.MinResultInches;
                }
            }
            return System.Math.Max(floor, baseChargeInches + delta);
        }
    }
}
