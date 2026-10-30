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
    ///
    /// #425: every query takes <c>attackerMoved</c> - whether the shooter moved this activation - because
    /// Core Rules v3.6.0 Indirect grants its line-of-sight and Sight-Blocker-cover facets only "when using Hold
    /// actions". Each caller passes the fact it holds: the shooting stages their metadata's
    /// <c>AttackerMoved</c>, the targeting builders the action context's <c>HasMoved</c>, the movement request
    /// <c>true</c> (it previews the shot AFTER the move), the AI its candidate's action.
    /// </summary>
    public static class SightRuleQueries
    {
        /// <summary>
        /// The alias-aware display name of the rule that makes the attacker's weapon ignore the target's
        /// cover (Blast and friends), or null if none does. Used by the resolvers to attribute the effect
        /// ("Flamethrower (Blast ignores cover)"). Non-logging — safe to call while building UI.
        /// </summary>
        public static string? CoverIgnoreSource(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator,
            bool attackerMoved)
            => FirstSource<RuleOperation.IgnoreCover>(attacker, weapon, evaluator, attackerMoved);

        public static bool IgnoresCover(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator, bool attackerMoved)
            => CoverIgnoreSource(attacker, weapon, evaluator, attackerMoved) != null;

        /// <summary>
        /// The alias-aware display name of the rule that makes the attacker's weapon ignore intervening
        /// terrain for line of sight (Indirect on a Hold, Quick Throw) — it may fire at targets it has no clear
        /// line to, as if in line of sight — or null if none does. (Takedown does NOT: it re-scopes the attack to
        /// a single model and nothing more — see #314. Distinct from Flying/Strider's
        /// <see cref="RuleOperation.IgnoreTerrainEffects"/>, which is MOVEMENT terrain, not LoS.) Non-logging.
        /// </summary>
        public static string? LineOfSightIgnoreSource(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator,
            bool attackerMoved)
            => FirstSource<RuleOperation.IgnoreLineOfSight>(attacker, weapon, evaluator, attackerMoved);

        /// <summary>
        /// Whether the attacker's weapon ignores intervening terrain for line of sight. Shared by the
        /// ranged-target enumeration, the occlusion stage, and the movement/targeting resolver builders so
        /// they agree on which targets stay shootable.
        /// </summary>
        public static bool IgnoresTerrain(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator, bool attackerMoved)
            => LineOfSightIgnoreSource(attacker, weapon, evaluator, attackerMoved) != null;

        /// <summary>
        /// #425: the alias-aware display name of the rule that makes the attacker's weapon ignore cover from Sight
        /// Blockers (Indirect on a Hold: "ignores Cover from Sight Blockers"), or null. Cover terrain is NOT
        /// ignored by it - see <see cref="CoverIgnoreSource"/> for the rules that drop all cover.
        /// </summary>
        public static string? SightBlockerCoverIgnoreSource(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator,
            bool attackerMoved)
            => FirstSource<RuleOperation.IgnoreSightBlockerCover>(attacker, weapon, evaluator, attackerMoved);

        /// <summary>
        /// #425: how this weapon's shot treats Sight Blockers in the cover test (<c>CoverMajority</c>) -
        /// <see cref="ESightBlockerCover.BlocksSight"/> for a shot that needs line of sight (the base rule),
        /// <see cref="ESightBlockerCover.Ignored"/> for one that ignores both line of sight and Sight-Blocker cover
        /// (Indirect on a Hold), <see cref="ESightBlockerCover.GrantsCover"/> for one that ignores line of sight but
        /// keeps Sight-Blocker cover (Quick Throw after an Advance). One evaluation pass.
        /// </summary>
        public static ESightBlockerCover SightBlockerCoverFor(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator,
            bool attackerMoved)
        {
            bool ignoresSight = false;
            bool ignoresBlockerCover = false;
            foreach ((RuleOperation op, string _) in Evaluate(attacker, weapon, evaluator, attackerMoved))
            {
                if (op is RuleOperation.IgnoreLineOfSight) ignoresSight = true;
                else if (op is RuleOperation.IgnoreSightBlockerCover) ignoresBlockerCover = true;
            }

            if (!ignoresSight) return ESightBlockerCover.BlocksSight;
            return ignoresBlockerCover ? ESightBlockerCover.Ignored : ESightBlockerCover.GrantsCover;
        }

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

        private static IReadOnlyList<(RuleOperation Op, string RuleName)> Evaluate(IUnit attacker, IWeapon weapon,
            RuleEvaluator evaluator, bool attackerMoved)
            => evaluator.EvaluateAllNamed(new CoverIgnoreContext(attacker, attackerMoved),
                RuleParticipant.Actor(attacker, weapon));

        private static string? FirstSource<TOp>(IUnit attacker, IWeapon weapon, RuleEvaluator evaluator,
            bool attackerMoved) where TOp : RuleOperation
        {
            foreach ((RuleOperation op, string ruleName) in Evaluate(attacker, weapon, evaluator, attackerMoved))
            {
                if (op is TOp) return ruleName;
            }
            return null;
        }
    }
}
