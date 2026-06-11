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
        /// <summary>
        /// The alias-aware display name of the rule that makes the attacker's weapon ignore the target's
        /// cover (Blast and friends), or null if none does. Used by the resolvers to attribute the effect
        /// ("Flamethrower (Blast ignores cover)"). Non-logging — safe to call while building UI.
        /// </summary>
        public static string? CoverIgnoreSource(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string ruleName) in evaluator.EvaluateAllNamed(
                         new CoverIgnoreContext(attacker), (attacker, ERuleSeat.Actor)))
            {
                if (op is RuleOperation.IgnoreCover) return ruleName;
            }
            return null;
        }

        public static bool IgnoresCover(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
            => CoverIgnoreSource(attacker, weapon, evaluator) != null;

        /// <summary>
        /// The alias-aware display name of the rule that makes the attacker's weapon ignore intervening
        /// terrain for line of sight (Indirect, Takedown) — it may fire at targets it has no clear line to,
        /// as if in line of sight — or null if none does. (Distinct from Flying/Strider's
        /// <see cref="RuleOperation.IgnoreTerrainEffects"/>, which is MOVEMENT terrain, not LoS.) Non-logging.
        /// </summary>
        public static string? LineOfSightIgnoreSource(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string ruleName) in evaluator.EvaluateAllNamed(
                         new CoverIgnoreContext(attacker), (attacker, ERuleSeat.Actor)))
            {
                if (op is RuleOperation.IgnoreLineOfSight) return ruleName;
            }
            return null;
        }

        /// <summary>
        /// Whether the attacker's weapon ignores intervening terrain for line of sight. Shared by the
        /// ranged-target enumeration, the occlusion stage, and the movement/targeting resolver builders so
        /// they agree on which targets stay shootable.
        /// </summary>
        public static bool IgnoresTerrain(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
            => LineOfSightIgnoreSource(attacker, weapon, evaluator) != null;
    }
}
