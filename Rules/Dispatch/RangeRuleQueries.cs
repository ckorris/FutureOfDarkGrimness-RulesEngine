using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

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
        /// Range, Actor seat, +), the defender's debuffs (Ranged Shrouding, Subject seat, −), and any
        /// range-extension MARK on the defender (see <see cref="MarkRangeModifiers"/>) — then floored.
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

            foreach ((int markDelta, int markFloor) in MarkRangeModifiers(defender, evaluator.RuleResolver))
            {
                delta += markDelta;
                if (markFloor > floor) floor = markFloor;
            }

            return System.Math.Max(floor, weapon.RangeInches + delta);
        }

        /// <summary>
        /// The range modifiers a MARK on <paramref name="target"/> would confer on whoever attacks it —
        /// the "+6\" range when shooting against it once" spells (#377: Eternal Guidance, Clearview
        /// Leaves). Marks are claimed (and spent) by <c>DetermineHitRollStage</c>, AFTER target legality,
        /// so the range check has to PEEK them here or the extension could never enable the shot it
        /// exists for. Structural, mirroring <see cref="ShootAfterRushRules.MarkGrantsShootAfterRush"/>:
        /// a marked rule contributes each <see cref="Effect.RangeModifier"/> it carries at
        /// <see cref="EHookID.Shooting_OnRangeCheck"/>, conditions unevaluated (the phrase rules gate on
        /// nothing) — and like that peek, asking must not spend the mark. A null resolver (bare-evaluator
        /// test, or a resume before #095 rehydration) yields nothing, the standard mark-read degrade.
        /// </summary>
        private static IEnumerable<(int Delta, int MinResultInches)> MarkRangeModifiers(
            IUnit target, IRuleResolver? resolver)
        {
            if (resolver == null) yield break;

            foreach (Token mark in target.Tokens.GetAllTokens(TokenType.Mark))
            {
                if (mark.Payload is not TokenPayload.RuleGrant grant) continue;
                if (!resolver.TryResolve(grant.RuleName, out ResolvedRule resolved)) continue;

                foreach (HookEntry entry in resolved.Definition.Passive)
                {
                    if (entry.HookID == EHookID.Shooting_OnRangeCheck
                        && entry.Effect is Effect.RangeModifier rangeModifier)
                    {
                        yield return (rangeModifier.Delta, rangeModifier.MinResultInches);
                    }
                }
            }
        }
    }
}
