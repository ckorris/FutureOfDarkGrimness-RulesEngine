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
        /// Seam for Indirect's "fire at non-LoS targets as if in line of sight" facet (W9), not yet
        /// implemented. No rule queues a shooting-terrain-ignore op today (Flying/Strider's
        /// <see cref="RuleOperation.IgnoreTerrainEffects"/> is a MOVEMENT-terrain rule, not a LoS one), so
        /// this is false until Indirect's LoS facet lands.
        /// </summary>
        public static bool IgnoresTerrain(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator) => false;
    }
}
