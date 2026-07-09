using System.Linq;
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
                         new MoveThroughEnemyContext(unit), RuleParticipant.Actor(unit)))
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
                         new MoveThroughTerrainContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is RuleOperation.IgnoreTerrainEffects) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="unit"/> ignores ALL terrain movement effects — the Flying scope of
        /// <see cref="RuleOperation.IgnoreTerrainEffects"/> (<see cref="ETerrainIgnoreScope.AllTerrain"/>),
        /// which additionally waives Dangerous-terrain wound rolls and Impassible-terrain blocking on top of
        /// the difficult-terrain cap. Strider (<see cref="ETerrainIgnoreScope.DifficultOnly"/>) returns false
        /// here. Non-logging — safe to call per-frame while building UI.
        /// </summary>
        public static bool IgnoresAllTerrain(IUnit unit, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new MoveThroughTerrainContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is RuleOperation.IgnoreTerrainEffects terrain && terrain.Scope == ETerrainIgnoreScope.AllTerrain)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="unit"/> currently counts as being in the given terrain for its move —
        /// the "counts as being in Difficult/Dangerous Terrain once" family (#153 spell corpus), granted
        /// one-shot and read here throughout the move (read-only: the grant is spent by
        /// <c>ExecuteMoveStage</c> when the move resolves, not by this query). Callers should let
        /// <see cref="IgnoresDifficultTerrain"/>/<see cref="IgnoresAllTerrain"/> take precedence: a bearer
        /// that ignores the real terrain effect ignores the counted-as one too.
        /// </summary>
        public static bool CountsAsInTerrain(IUnit unit, RuleEvaluator evaluator, ECountAsTerrain kind)
        {
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new MoveThroughTerrainContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is RuleOperation.CountAsInTerrain counted && counted.Terrain == kind) return true;
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
                         RuleParticipant.Actor(charger),
                         // #183: the charged unit's living models surface a joined hero's relocated Melee
                         // Shrouding / Darkborn (Defensive) charge debuff, gated by AllModelsHaveThisRule.
                         RuleParticipant.Subject(target, models: HeroStatRules.LivingModels(target))))
            {
                if (op is RuleOperation.ApplyMovementBonus move && move.ActionType == EActionType.Charge)
                {
                    delta += move.DistanceInches;
                    if (move.MinResultInches > floor) floor = move.MinResultInches;
                }
            }
            return System.Math.Max(floor, baseChargeInches + delta);
        }

        /// <summary>
        /// The distance <paramref name="unit"/> may move and still shoot (Advance), including its movement
        /// rules (Fast/Quick/Rapid Advance/...). Mirrors <c>MovementActionContext</c>'s unit-scalar Advance
        /// computation exactly — the base <see cref="GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES"/> plus the
        /// <see cref="MovementModifierSink"/> net for Advance, fed by the <see cref="Contexts.MoveActionDeclaredContext"/>
        /// "when" fired for all three actions onto one sink (as the context does) — so the shoot-after-advance
        /// gate (<c>ChooseActionStage.GetCanShoot</c>) agrees with the distance the move resolver actually
        /// granted. Without this a Fast unit that legally advanced past the base 6" is wrongly blocked from
        /// shooting, because the stubbed <c>GetMobility</c> returns only the base. Non-logging (safe per-frame).
        /// (The situational difficult-terrain cap is intentionally not applied here: the resolver already caps
        /// the actual move, so the moved distance is always &lt;= this allowance regardless.)
        /// </summary>
        public static float EffectiveMoveShootDistance(IUnit unit, RuleEvaluator evaluator)
        {
            MovementModifierSink sink = new MovementModifierSink();
            AccumulateMovementRules(unit, evaluator, EActionType.Advance, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES, sink);
            AccumulateMovementRules(unit, evaluator, EActionType.Rush, GameWideConstants.RUSH_DISTANCE_INCHES, sink);
            AccumulateMovementRules(unit, evaluator, EActionType.Charge, GameWideConstants.CHARGE_DISTANCE_INCHES, sink);
            return GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES + sink.Net(EActionType.Advance);
        }

        /// <summary>
        /// The longest distance any model of <paramref name="unit"/> may legally move WITHOUT the move
        /// having to end in melee: the unit's Rush allowance including its movement rules
        /// (Agile/Slow/...), or a model's own larger Rush budget where a per-model rule raises it
        /// (#093, a joined hero). Mirrors <c>MovementActionContext</c>'s unit-scalar and per-model Rush
        /// computations, for the same reason <see cref="EffectiveMoveShootDistance"/> exists: the Pass
        /// gate (<c>ChooseActionStage.GetCanPass</c>) must agree with the distance the move resolver
        /// actually granted. Comparing against the bare default instead wrongly locks a rush-boosted
        /// unit that legally moved past the base 12" out of Pass ("must engage in melee"), leaving only
        /// its layered actions (e.g. Cast) until they run dry. Non-logging - safe to call per-frame.
        /// (The situational difficult-terrain cap is intentionally not applied: the resolver already
        /// caps the actual move, so the moved distance is always &lt;= this allowance regardless.)
        /// </summary>
        public static float EffectiveMaxRushDistance(IUnit unit, RuleEvaluator evaluator)
        {
            MovementModifierSink unitSink = new MovementModifierSink();
            AccumulateMovementRules(unit, evaluator, EActionType.Advance, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES, unitSink);
            AccumulateMovementRules(unit, evaluator, EActionType.Rush, GameWideConstants.RUSH_DISTANCE_INCHES, unitSink);
            AccumulateMovementRules(unit, evaluator, EActionType.Charge, GameWideConstants.CHARGE_DISTANCE_INCHES, unitSink);
            float maxRush = GameWideConstants.RUSH_DISTANCE_INCHES + unitSink.Net(EActionType.Rush);

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive())
                {
                    continue;
                }

                MovementModifierSink modelSink = new MovementModifierSink();
                AccumulateModelMovementRules(unit, model, evaluator, EActionType.Advance, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES, modelSink);
                AccumulateModelMovementRules(unit, model, evaluator, EActionType.Rush, GameWideConstants.RUSH_DISTANCE_INCHES, modelSink);
                AccumulateModelMovementRules(unit, model, evaluator, EActionType.Charge, GameWideConstants.CHARGE_DISTANCE_INCHES, modelSink);
                float modelRush = GameWideConstants.RUSH_DISTANCE_INCHES + modelSink.Net(EActionType.Rush);
                if (modelRush > maxRush)
                {
                    maxRush = modelRush;
                }
            }

            return maxRush;
        }

        private static void AccumulateMovementRules(IUnit unit, RuleEvaluator evaluator, EActionType action,
            float baseDistance, MovementModifierSink sink)
        {
            var operations = evaluator.EvaluateAllNamed(
                new MoveActionDeclaredContext(unit, action, baseDistance), RuleParticipant.Actor(unit));
            sink.ApplyFrom(operations.Select(t => t.Op));
        }

        // Like AccumulateMovementRules but folds the given model's own rules (AnyOwner union) so a
        // per-model Fast/Slow lands in that model's budget only — mirrors MovementActionContext's
        // per-model accumulation. Read-only (EvaluateAllNamed): must not consume one-shot grants.
        private static void AccumulateModelMovementRules(IUnit unit, IModel model, RuleEvaluator evaluator,
            EActionType action, float baseDistance, MovementModifierSink sink)
        {
            var operations = evaluator.EvaluateAllNamed(
                new MoveActionDeclaredContext(unit, action, baseDistance),
                RuleParticipant.Actor(unit, models: new[] { model }));
            sink.ApplyFrom(operations.Select(t => t.Op));
        }
    }
}
