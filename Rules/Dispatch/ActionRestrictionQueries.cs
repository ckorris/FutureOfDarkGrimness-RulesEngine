using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #425: "what may this unit do this activation" - the answers rules queue at
    /// <see cref="EHookID.Activation_OnActionChoice"/>, read in ONE place so the action menu
    /// (<c>ChooseActionStage</c>), the move budget (<c>MovementActionContext</c>,
    /// <see cref="MovementRuleQueries"/>) and the Tactician's planning cannot disagree.
    /// <list type="bullet">
    /// <item><see cref="AllowedActions"/> - the <see cref="RuleOperation.RestrictActions"/> sets, intersected
    /// (Immobile: Hold; Cumbersome: Hold or Advance). Null when nothing restricts.</item>
    /// <item><see cref="ShootingAfterMovingForbiddenBy"/> - Cumbersome's "can't shoot when moving"
    /// (<see cref="RuleOperation.ForbidShootingAfterMoving"/>).</item>
    /// </list>
    /// Detected by the operation a rule produces, never by its name, so an aliased or renamed copy behaves
    /// identically (the <see cref="ShootAfterRushRules"/> convention). Non-logging (<c>EvaluateAllNamed</c>) -
    /// safe while building UI or an AI plan.
    /// </summary>
    public static class ActionRestrictionQueries
    {
        /// <summary>
        /// The action types <paramref name="unit"/> may declare - every <see cref="RuleOperation.RestrictActions"/>
        /// it carries, intersected (two restrictions both bind) - or null when no rule restricts it (the common
        /// case). <paramref name="restrictingRule"/> is the alias-aware name of the first restricting rule, for the
        /// menu's reason text.
        /// </summary>
        public static IReadOnlySet<EActionType>? AllowedActions(IUnit unit, RuleEvaluator evaluator,
            out string? restrictingRule)
        {
            restrictingRule = null;
            HashSet<EActionType>? allowed = null;
            foreach ((RuleOperation op, string ruleName) in evaluator.EvaluateAllNamed(
                         new ActionChoiceContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is not RuleOperation.RestrictActions restrict) continue;

                restrictingRule ??= ruleName;
                HashSet<EActionType> these = new HashSet<EActionType>(restrict.Allowed);
                allowed = allowed == null
                    ? these
                    : new HashSet<EActionType>(allowed.Where(these.Contains));
            }
            return allowed;
        }

        /// <summary> <see cref="AllowedActions"/> without the rule name. </summary>
        public static IReadOnlySet<EActionType>? AllowedActions(IUnit unit, RuleEvaluator evaluator)
            => AllowedActions(unit, evaluator, out _);

        /// <summary>Whether <paramref name="unit"/> may declare <paramref name="action"/> at all.</summary>
        public static bool MayDeclare(IUnit unit, RuleEvaluator evaluator, EActionType action)
            => AllowedActions(unit, evaluator)?.Contains(action) ?? true;

        /// <summary>
        /// The alias-aware name of the rule that forbids <paramref name="unit"/> to shoot once it has moved this
        /// activation (Cumbersome), or null when nothing does.
        /// </summary>
        public static string? ShootingAfterMovingForbiddenBy(IUnit unit, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string ruleName) in evaluator.EvaluateAllNamed(
                         new ActionChoiceContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is RuleOperation.ForbidShootingAfterMoving) return ruleName;
            }
            return null;
        }

        /// <summary>
        /// Applies an allowed-action set to a move budget: no Rush caps the Rush at the Advance, and no Charge caps
        /// the Charge at the (capped) Rush - so a Cumbersome unit's Move action is an Advance and nothing longer,
        /// and its hard move cap (max of Rush and Charge, <c>DefinePathStage</c>) is its Advance too. The one
        /// formula <c>MovementActionContext</c> and <see cref="MovementRuleQueries"/> both apply.
        /// </summary>
        public static (float Advance, float Rush, float Charge) CapBudget(IReadOnlySet<EActionType>? allowed,
            float advance, float rush, float charge)
        {
            if (allowed == null) return (advance, rush, charge);
            if (!allowed.Contains(EActionType.Rush)) rush = System.Math.Min(rush, advance);
            if (!allowed.Contains(EActionType.Charge)) charge = System.Math.Min(charge, rush);
            return (advance, rush, charge);
        }
    }
}
