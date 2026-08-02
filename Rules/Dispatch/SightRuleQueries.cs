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
    /// Per-weapon (#027): the queried weapon's own rules are evaluated alongside the attacker's unit
    /// rules, so a unit with one Indirect mortar and one plain rifle reports each weapon accurately.
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
                         new CoverIgnoreContext(attacker), RuleParticipant.Actor(attacker, weapon)))
            {
                if (op is RuleOperation.IgnoreCover) return ruleName;
            }
            return null;
        }

        public static bool IgnoresCover(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
            => CoverIgnoreSource(attacker, weapon, evaluator) != null;

        /// <summary>
        /// The alias-aware display name of the rule that makes the attacker's weapon ignore intervening
        /// terrain for line of sight (Indirect) — it may fire at targets it has no clear line to,
        /// as if in line of sight — or null if none does. (Takedown does NOT: it re-scopes the attack to a
        /// single model and nothing more — see #313. Distinct from Flying/Strider's
        /// <see cref="RuleOperation.IgnoreTerrainEffects"/>, which is MOVEMENT terrain, not LoS.) Non-logging.
        /// </summary>
        public static string? LineOfSightIgnoreSource(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string ruleName) in evaluator.EvaluateAllNamed(
                         new CoverIgnoreContext(attacker), RuleParticipant.Actor(attacker, weapon)))
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

        /// <summary>
        /// Whether this weapon's attack re-scopes to a single chosen model in the target unit (Takedown,
        /// #157). Used by ChooseRangedAttackStage to fire the weapon's copies as SEPARATE single-shot
        /// attacks, so each shot picks its own victim. Non-consuming — BuildTargetListStage's own per-attack
        /// evaluation still drives the actual pick (and spends any one-shot grant there, not here).
        /// </summary>
        public static bool TargetsIndividualModels(IUnit attacker, IWeapon weapon, IUnit defender,
            RuleEvaluator evaluator)
            => IndividualTargetSource(attacker, weapon, defender, evaluator) != null;

        /// <summary>
        /// The alias-aware display name of the rule that re-scopes this weapon's attack to a single chosen
        /// model (Takedown), or null if none does. Named variant of
        /// <see cref="TargetsIndividualModels"/> — the ranged picker attributes its resolve-first gating
        /// with it ("Must fire Takedown weapons first."), so an army that renames Takedown shows its own
        /// name. Non-logging and non-consuming (<c>EvaluateAllNamed</c>), so it is safe to call from the
        /// option builder, which runs on every Choose Action / Choose Weapon pass.
        /// </summary>
        public static string? IndividualTargetSource(IUnit attacker, IWeapon weapon, IUnit defender,
            RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string ruleName) in evaluator.EvaluateAllNamed(
                         new ShootTargetsSelectedContext(attacker, defender), RuleParticipant.Actor(attacker, weapon)))
            {
                if (op is RuleOperation.TargetIndividualModel) return ruleName;
            }
            return null;
        }
    }
}
