using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Derives, from a unit's #042 rules, whether its attacks ignore the target's cover (Blast) or
    /// intervening terrain (Indirect). Single source of truth shared by the cover stage (to drop the
    /// cover bonus), the ranged-target option builder, and the movement request builder — so the
    /// "ignores cover / ignores terrain" flags they surface to the resolvers stay consistent.
    ///
    /// Currently unit-scoped: Blast/Indirect are modelled as unit rules, so the result applies to all of
    /// the unit's weapons. The signatures take a <see cref="Weapon"/> so that when weapon-scoped rules
    /// land the derivation can tighten per-weapon without callers changing shape; today the weapon is
    /// unused.
    /// </summary>
    public static class SightRuleQueries
    {
        public static bool IgnoresCover(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
        {
            IReadOnlyList<RuleOperation> ops = evaluator.EvaluateAll(
                new CoverIgnoreContext(attacker), (attacker, ERuleSeat.Actor));
            return ops.OfType<RuleOperation.IgnoreCover>().Any();
        }

        /// <summary>
        /// Whether the attacker's weapon ignores intervening terrain for line of sight — it may fire at
        /// targets it has no clear line to, as if in line of sight. Covers Indirect's "target non-LoS as
        /// if LoS" facet and Takedown's "ignore intervening LoS" facet, both queuing
        /// <see cref="RuleOperation.IgnoreLineOfSight"/>. (Distinct from Flying/Strider's
        /// <see cref="RuleOperation.IgnoreTerrainEffects"/>, which is MOVEMENT terrain, not LoS.) Shared by
        /// the ranged-target enumeration, the occlusion stage, and the movement/targeting resolver builders
        /// so they agree on which targets stay shootable.
        /// </summary>
        public static bool IgnoresTerrain(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
        {
            IReadOnlyList<RuleOperation> ops = evaluator.EvaluateAll(
                new CoverIgnoreContext(attacker), (attacker, ERuleSeat.Actor));
            return ops.OfType<RuleOperation.IgnoreLineOfSight>().Any();
        }
    }
}
