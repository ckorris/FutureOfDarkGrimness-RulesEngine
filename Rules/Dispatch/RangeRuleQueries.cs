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
        /// Range, Highborn, Actor seat, +), the defender's debuffs (Ranged Shrouding, Darkborn, Subject seat, −),
        /// and any range-extension MARK on the defender (see <see cref="MarkRangeModifiers"/>) — then floored.
        /// The floor is the largest <c>MinResultInches</c> among the active REDUCTIONS (e.g. Darkborn's "−4\" to a
        /// min. of 6\""), and it only ever limits a reduction: a weapon whose range (after the buffs) is already
        /// under the floor keeps that range rather than being lengthened to it (#432 - a 9" gun against Darkborn
        /// stays 6", a 4" gun stays 4"). Never below 0. A charge-only range rule (Melee Shrouding: delta 0) leaves
        /// shooting alone. Per-weapon (#027): the firing weapon's own rules are evaluated alongside the attacker's
        /// unit rules. Non-logging — safe to call per-frame while building UI.
        /// </summary>
        public static float EffectiveRange(IUnit attacker, IWeapon weapon, IUnit defender, RuleEvaluator evaluator)
        {
            var fold = new RangeFold();
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new RangeModifierContext(attacker),
                         RuleParticipant.Actor(attacker, weapon),
                         // #183: the defender's living models surface a joined hero's relocated Ranged
                         // Shrouding / Darkborn (Defensive), gated by MostModelsHaveThisRule.
                         RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender))))
            {
                if (op is RuleOperation.ApplyRangeModifier rangeModifier)
                {
                    fold.Add(rangeModifier.Delta, rangeModifier.MinResultInches);
                }
            }

            foreach ((int markDelta, int markFloor) in MarkRangeModifiers(defender, evaluator.RuleResolver))
            {
                fold.Add(markDelta, markFloor);
            }

            return fold.Apply(weapon.RangeInches);
        }

        /// <summary>
        /// #432: the CHARGE range change <paramref name="charger"/>'s own range rules give it - the Actor-seat
        /// <see cref="RuleOperation.ApplyRangeModifier.ChargeDelta"/> at <see cref="EHookID.Shooting_OnRangeCheck"/>
        /// (Highborn / Extra Reach / Royal Legion: "+4\"/+2\" range when shooting/charging" -> +2). Charge range is
        /// the charge move, so every charge-budget reader adds this to the Charge net beside the movement rules
        /// (<c>MovementActionContext</c> and its per-model budgets, <see cref="MovementRuleQueries.PerModelMoveBudgets"/>,
        /// the AI's <c>TacticalAnalysis.ChargeBudget</c>). <paramref name="model"/> folds that model's own rules too
        /// (a joined hero's Highborn lands in its budget only), mirroring the movement accumulators. Weapon rules are
        /// not consulted - a charge has no firing weapon. No floor: no Actor-seat charge reduction exists. Non-logging.
        /// </summary>
        public static float OwnChargeRangeDelta(IUnit charger, RuleEvaluator evaluator, IModel? model = null)
        {
            RuleParticipant actor = model == null
                ? RuleParticipant.Actor(charger)
                : RuleParticipant.Actor(charger, models: new[] { model });

            int delta = 0;
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(new RangeModifierContext(charger), actor))
            {
                if (op is RuleOperation.ApplyRangeModifier rangeModifier)
                {
                    delta += rangeModifier.ChargeDelta;
                }
            }

            return delta;
        }

        /// <summary>
        /// #432: the CHARGE range changes <paramref name="target"/>'s rules impose on a unit charging it - the
        /// Subject-seat <see cref="RuleOperation.ApplyRangeModifier.ChargeDelta"/>s (Darkborn's −2, Melee Shrouding's
        /// −2, each "to a min. of 6\"") - as (positive, negative, floor). Only the Subject participant is evaluated:
        /// the charger's own charge range is already in the budget the caller passes
        /// (<see cref="OwnChargeRangeDelta"/>). Read by <see cref="MovementRuleQueries.EffectiveChargeDistanceAgainst"/>.
        /// </summary>
        internal static (int Positive, int Negative, int Floor) TargetChargeRangeModifiers(IUnit charger, IUnit target,
            RuleEvaluator evaluator)
        {
            var fold = new RangeFold();
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new RangeModifierContext(charger),
                         RuleParticipant.Subject(target, models: HeroStatRules.LivingModels(target))))
            {
                if (op is RuleOperation.ApplyRangeModifier rangeModifier)
                {
                    fold.Add(rangeModifier.ChargeDelta, rangeModifier.MinResultInches);
                }
            }

            return (fold.Positive, fold.Negative, fold.Floor);
        }

        /// <summary>
        /// #432: the one floor arithmetic both halves of the range family share. Buffs and reductions are summed
        /// apart; the floor is the largest <c>MinResultInches</c> among the REDUCTIONS (a zero delta contributes
        /// nothing - a charge-only op must not floor shooting); the result is the reduced range, but never below
        /// the floor unless the buffed range was already below it (then that range), and never below 0.
        /// </summary>
        internal struct RangeFold
        {
            public int Positive;
            public int Negative;
            public int Floor;

            public void Add(int delta, int minResultInches)
            {
                if (delta > 0)
                {
                    Positive += delta;
                }
                else if (delta < 0)
                {
                    Negative += delta;
                    if (minResultInches > Floor) Floor = minResultInches;
                }
            }

            public readonly float Apply(float baseInches) => ApplyTo(baseInches, Positive, Negative, Floor);

            public static float ApplyTo(float baseInches, float positive, float negative, float floor)
            {
                float buffed = baseInches + positive;
                return System.Math.Max(0f, System.Math.Max(buffed + negative, System.Math.Min(floor, buffed)));
            }
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
