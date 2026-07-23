using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P23 — where a caster may cast FROM. <c>Spell Conduit</c>: "casters within 12" that are from
    /// other friendly units may cast spells as if they were in this model's position, and get +1 to casting
    /// rolls when doing so."
    ///
    /// <para>A relay changes only the point a spell's range and line of sight are measured from, plus the
    /// cast roll. It does NOT move the caster: the #103 assist scan still measures 18" from the casting
    /// unit, and the caster's own eligibility (has it attacked, is it embarked) is unchanged. The narrow
    /// reading, and the one the rule's wording supports - it relays the spell, not the caster.</para>
    ///
    /// <para><b>No prompt: the origin is derived, not chosen.</b> A relay origin is never worse than the
    /// caster's own - it can only add reach, and it adds the bonus - so the only question is which origin
    /// reaches the targets the player picked. <c>CastSpellStage</c> therefore offers the UNION of every
    /// origin's legal targets, narrows the viable origins as targets are chosen, and casts from a relay if
    /// one still covers them all. Which origin a target will use is spelled out on its row in the target
    /// list, so the bonus is something the player can aim for rather than something that happens to
    /// them.</para>
    /// </summary>
    public static class SpellRelay
    {
        /// <summary>
        /// One place a cast can originate from: the caster itself (<see cref="IsSelf"/>, no bonus) or a
        /// relaying unit. <see cref="RollBonus"/> applies only to a cast actually made from here.
        /// </summary>
        public readonly record struct CastOrigin(IUnit Unit, int RollBonus, bool IsSelf)
        {
            /// <summary>"Synaptic Relay (+1)", or the caster's own name — for target rows and log lines.</summary>
            public string Describe() => IsSelf ? Unit.Name : $"{Unit.Name} (+{RollBonus})";
        }

        /// <summary>
        /// Every origin available to <paramref name="caster"/> right now, relays first so a caller that
        /// takes the first viable one prefers the bonus. The caster's own position is always last and
        /// always present: a cast can always be made from where the caster stands.
        /// </summary>
        public static IReadOnlyList<CastOrigin> OriginsFor(ITableState tableState, RuleEvaluator evaluator,
            IUnit caster)
        {
            List<CastOrigin> origins = new List<CastOrigin>();

            foreach (CastSupport.Neighbour neighbour in CastSupport.NeighboursOf(tableState, caster))
            {
                foreach (RuleOperation.EnableSpellRelay relay in
                         CapabilityRuleQueries.RelayOffers(neighbour.Unit, evaluator))
                {
                    if (neighbour.DistanceInches > relay.RangeInches) continue;

                    origins.Add(new CastOrigin(neighbour.Unit, relay.CastRollBonus, IsSelf: false));
                }
            }

            origins.Add(new CastOrigin(caster, RollBonus: 0, IsSelf: true));
            return origins;
        }

        /// <summary>
        /// True when any origin other than the caster's own is available — the cheap test for "is there
        /// anything to tell the player about", used to decide whether the spell picker carries a relay note.
        /// </summary>
        public static bool AnyRelayAvailable(IReadOnlyList<CastOrigin> origins)
        {
            foreach (CastOrigin origin in origins)
            {
                if (!origin.IsSelf) return true;
            }

            return false;
        }
    }
}
