using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Derives casting CAPABILITIES from a unit's #042 rules. Mirrors <see cref="RangeRuleQueries"/> /
    /// <see cref="SightRuleQueries"/> / <see cref="MovementRuleQueries"/>: a single non-logging read of the
    /// rule dispatch, shared by every stage that needs the answer, so they cannot drift apart.
    ///
    /// <para>Casting is asked for, not tested for. The stages used to decide "is this a caster?" by
    /// comparing against the core <c>Caster</c> definition, which is an identity check standing in for a
    /// capability — it excludes any other rule that confers casting (<c>Caster Group</c> is not
    /// <c>Caster</c>, and could never be granted as one: its X is a live model count and granted rules
    /// carry no arguments), and it cannot express a capability that depends on live state. Asking the rule
    /// graph fixes both: a rule opts in by authoring <see cref="Effect.EnableCasting"/>, its
    /// <see cref="Condition"/> gates the answer, and suppression applies as it does anywhere else.</para>
    /// </summary>
    public static class CastingRuleQueries
    {
        /// <summary>
        /// Whether <paramref name="unit"/> can cast spells: true when any rule it carries answers the
        /// <see cref="EHookID.Casting_OnCastCapability"/> question with
        /// <see cref="RuleOperation.EnableCasting"/>.
        ///
        /// <para>The unit's living MODELS are named as participants alongside the unit (#093), so a joined
        /// Caster hero — whose rule the #006 hero-merge relocated onto its model — still makes the host unit
        /// a caster. This is the same accommodation
        /// <c>StartOfRoundExtraActionStage.GrantSpellTokens</c> makes when funding the pool; the two must
        /// agree, or a unit gets tokens it can't spend (or the reverse).</para>
        ///
        /// Non-logging and side-effect-free — safe to call per-frame while building UI, and safe to call
        /// repeatedly while scanning every unit on the table for cast assisters.
        /// </summary>
        public static bool CanCast(IUnit unit, RuleEvaluator evaluator)
        {
            foreach (RuleOperation op in evaluator.Evaluate(unit, ERuleSeat.Actor,
                         new CastCapabilityContext(unit), weapon: null, models: unit.Models))
            {
                if (op is RuleOperation.EnableCasting)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
