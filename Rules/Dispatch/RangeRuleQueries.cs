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
        /// The net range delta (inches) for <paramref name="attacker"/> firing <paramref name="weapon"/> at
        /// <paramref name="defender"/>, summing every <see cref="RuleOperation.ApplyRangeModifier"/>:
        /// the attacker's own buffs (Increased Shooting Range, Actor seat, +) and the defender's debuffs
        /// (Ranged Shrouding, Subject seat, −). Add it to <c>weapon.RangeInches</c> (the caller floors the
        /// result at 0). Per-weapon (#027): the firing weapon's own rules are evaluated alongside the
        /// attacker's unit rules. Non-logging — safe to call per-frame while building UI.
        /// </summary>
        public static int EffectiveRangeDelta(IUnit attacker, IWeapon weapon, IUnit defender, RuleEvaluator evaluator)
        {
            int delta = 0;
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new RangeModifierContext(attacker),
                         (attacker, ERuleSeat.Actor, weapon),
                         (defender, ERuleSeat.Subject, (IWeapon?)null)))
            {
                if (op is RuleOperation.ApplyRangeModifier rangeModifier) delta += rangeModifier.Delta;
            }
            return delta;
        }
    }
}
