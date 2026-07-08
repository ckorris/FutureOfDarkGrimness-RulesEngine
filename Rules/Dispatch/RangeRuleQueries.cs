using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Derives, from a unit's #042 rules, the net change to a weapon's effective shooting range. Mirrors
    /// <see cref="SightRuleQueries"/> and <see cref="MovementRuleQueries"/>: a single non-logging read of the
    /// rule dispatch that the range check shares, so the engine target-eligibility check (and any resolver
    /// that wants to preview it) agree on how far a weapon reaches.
    /// </summary>
    public static class RangeRuleQueries
    {
        /// <summary>
        /// The effective shooting range (inches) for <paramref name="attacker"/> firing
        /// <paramref name="weapon"/> at <paramref name="defender"/>: the weapon's base range plus every
        /// <see cref="RuleOperation.ApplyRangeModifier"/> delta — the attacker's own buffs (Increased Shooting
        /// Range, Actor seat, +) and the defender's debuffs (Ranged Shrouding, Subject seat, −) — then floored.
        /// The floor is the largest <c>MinResultInches</c> among the active modifiers (e.g. Ranged Shrouding's
        /// "−6\" to a min. of 6\""), and never below 0, so a reduction can't push range below the rule's
        /// minimum (nor negative). Per-weapon (#027): the firing weapon's own rules are evaluated alongside the
        /// attacker's unit rules. Non-logging — safe to call per-frame while building UI.
        /// </summary>
        public static float EffectiveRange(IUnit attacker, IWeapon weapon, IUnit defender, RuleEvaluator evaluator)
        {
            int delta = 0;
            int floor = 0;
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new RangeModifierContext(attacker),
                         RuleParticipant.Actor(attacker, weapon),
                         // #183: the defender's living models surface a joined hero's relocated Ranged
                         // Shrouding / Darkborn (Defensive), gated by AllModelsHaveThisRule.
                         RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender))))
            {
                if (op is RuleOperation.ApplyRangeModifier rangeModifier)
                {
                    delta += rangeModifier.Delta;
                    if (rangeModifier.MinResultInches > floor) floor = rangeModifier.MinResultInches;
                }
            }
            return System.Math.Max(floor, weapon.RangeInches + delta);
        }
    }
}
