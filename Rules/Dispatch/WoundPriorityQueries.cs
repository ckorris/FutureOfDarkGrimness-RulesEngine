using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Derives, from a weapon's #042 rules, whether it must be resolved before the bearer's other
    /// weapons this attack — i.e. it carries a wound-multiplier rule (Deadly). The rulebook says these
    /// weapons are "used first" so a Deadly clump removes whole models before normal wounds are spread
    /// across the unit (#028). Capability-based: any rule that nets a wound multiplier &gt; 1 at
    /// pre-apply-wound qualifies, so this is not tied to the literal name "Deadly" and a future
    /// resolve-first wound rule is picked up automatically. Shared by the ranged and melee
    /// weapon-choice stages so they gate identically.
    ///
    /// Shooting has a second, non-wound resolve-first rule — Takedown's "Takedown attacks must be
    /// resolved before other weapons" (#313) — so the ranged picker asks
    /// <see cref="ShootingResolveFirstSource"/> instead, which is this predicate widened by the
    /// individual-target capability. Both rules land in ONE priority class: neither rulebook text claims
    /// precedence over the other, so a unit carrying both must fire both before its ordinary weapons and
    /// picks the order between them itself.
    /// </summary>
    public static class WoundPriorityQueries
    {
        /// <summary>
        /// True if <paramref name="weapon"/>, fired by <paramref name="attacker"/>, nets a wound
        /// multiplier above 1 and therefore must be chosen before the unit's non-multiplying weapons.
        /// Non-logging (mirrors <see cref="SightRuleQueries"/>) — safe to call while building the picker.
        /// </summary>
        public static bool MustResolveFirst(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
            => ResolveFirstSource(attacker, weapon, evaluator) != null;

        /// <summary>
        /// The alias-aware display name of the wound-multiplier rule that forces this weapon to be
        /// resolved first, or null if none does. Named variant of <see cref="MustResolveFirst"/>, so the
        /// picker can attribute its gating ("Must fire Deadly(3) weapons first.").
        /// </summary>
        public static string? ResolveFirstSource(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
        {
            // Deadly's condition is unconditional and the multiplier ignores the defender, so the
            // defender is irrelevant to priority; the attacker is passed as a neutral stand-in. (If a
            // future wound-multiplier rule becomes defender-specific, this query needs the real target.)
            foreach ((RuleOperation op, string ruleName) in evaluator.EvaluateAllNamed(
                         new PreApplyWoundContext(attacker, attacker),
                         RuleParticipant.Actor(attacker, weapon)))
            {
                // Same take-the-best threshold WoundModifierSink folds to: only a multiplier ABOVE 1
                // reorders anything, so a MultiplyWounds(1) op is not a priority source.
                if (op is RuleOperation.MultiplyWounds multiply && multiply.Multiplier > 1) return ruleName;
            }
            return null;
        }

        /// <summary>
        /// The display name of the rule that forces this weapon to be fired before the unit's other
        /// weapons when SHOOTING — a wound multiplier (Deadly, #028) or an individual-target re-scope
        /// (Takedown, #313) — or null if none does. Melee deliberately keeps the wound-only
        /// <see cref="MustResolveFirst"/>: Takedown's hook is
        /// <c>Shooting_OnShootTargetsSelected</c>, so a melee weapon carrying it re-scopes nothing and
        /// must not gate the unit's other melee weapons.
        ///
        /// The defender is the neutral attacker stand-in for the same reason Deadly uses one — Takedown's
        /// condition is unconditional. A future individual-target rule gated on the DEFENDER would need
        /// the real target threaded here (the gate is per weapon row, not per target row).
        /// </summary>
        public static string? ShootingResolveFirstSource(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
            => ResolveFirstSource(attacker, weapon, evaluator)
               ?? SightRuleQueries.IndividualTargetSource(attacker, weapon, attacker, evaluator);
    }
}
