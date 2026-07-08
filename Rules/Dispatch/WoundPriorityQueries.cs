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
    /// </summary>
    public static class WoundPriorityQueries
    {
        /// <summary>
        /// True if <paramref name="weapon"/>, fired by <paramref name="attacker"/>, nets a wound
        /// multiplier above 1 and therefore must be chosen before the unit's non-multiplying weapons.
        /// Non-logging (mirrors <see cref="SightRuleQueries"/>) — safe to call while building the picker.
        /// </summary>
        public static bool MustResolveFirst(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
        {
            // Deadly's condition is unconditional and the multiplier ignores the defender, so the
            // defender is irrelevant to priority; the attacker is passed as a neutral stand-in. (If a
            // future wound-multiplier rule becomes defender-specific, this query needs the real target.)
            IEnumerable<RuleOperation> operations = evaluator.EvaluateAllNamed(
                    new PreApplyWoundContext(attacker, attacker),
                    RuleParticipant.Actor(attacker, weapon))
                .Select(tagged => tagged.Op);

            WoundModifierSink sink = new WoundModifierSink();
            sink.ApplyFrom(operations);
            return sink.NetMultiplier > 1;
        }
    }
}
