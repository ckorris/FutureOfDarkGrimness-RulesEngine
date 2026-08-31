using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #381 (AoF Retreating Strike): the shared move-end strike offer, called by both trigger stages -
    /// <c>RetreatingStrikeMoveStage</c> (the unit's own move action) and
    /// <c>RetreatingStrikePostCombatStage</c> (a post-combat Harassing-family move, melee or shoot
    /// funnel). Fires the <see cref="EHookID.Movement_OnMoveResolved"/> hook for the mover and resolves
    /// any <see cref="Effect.DealAutoWounds"/> ability offered there: pick one enemy inside the ability's
    /// range (optional - cancelling declines and pays nothing), pay the cost, roll the pool, and hand the
    /// successes back as unsaveable wounds for the caller's assign/apply child pipeline (the
    /// <c>CrossingAttackStage</c> shape: no save stages, Regeneration/Tough still apply).
    ///
    /// <para>Owner ruling 2026-08-31 (the #381 trigger fork; informed by the official Discord thread on
    /// Harassing + Retreating Strike): "ends its move" means the end of any move the unit CHOOSES to
    /// make while it has been in melee this round (<see cref="Rules.Foundation.TokenType.WasInMeleeThisRound"/>) -
    /// the Harassing-family post-combat move ("Harassing fires first", then the strike reads the final
    /// positions) or its own later move action. The charger's forced 1" move-back is NOT a trigger, and
    /// Shaken blocks the strike - both authored as data on the rule def (the was-in-melee token gate and
    /// a Not(TokenPresent(Shaken)) condition), not enforced here. The engine's job is only to light the
    /// hook at the right seams; which rules fire there is the supplement's business.</para>
    /// </summary>
    internal static class RetreatingStrikeResolution
    {
        /// <summary>The rolled outcome of an accepted strike: who gets hurt, and by how much.</summary>
        public readonly struct StrikeResult
        {
            public DataBinding<UnitData> Target { get; }
            public string RuleName { get; }
            public float WoundCount { get; }

            public StrikeResult(DataBinding<UnitData> target, string ruleName, float woundCount)
            {
                Target = target;
                RuleName = ruleName;
                WoundCount = woundCount;
            }
        }

        /// <summary>
        /// The wound pipeline's child context for an accepted strike: a weaponless synthetic AP-0 attack
        /// carrying PRE-FAILED saves, so no save is rolled and only the defender's Regeneration/Tough
        /// apply (the CrossingAttackStage shape). Shared by both trigger stages.
        /// </summary>
        public static ICombatMetadata BuildWoundMetadata(IGameContext gameContext,
            DataBinding<UnitData> mover, StrikeResult strike)
        {
            Weapon strikeWeapon = new Weapon(strike.RuleName, rangeInches: 0f, attacks: 0,
                armorPenetration: 0);

            CombatMetadata metadata = new CombatMetadata(gameContext, mover, strike.Target,
                strikeWeapon, weaponCount: 1, isMelee: false);

            metadata.AddResult(SyntheticWoundResolution.AsUnsavedWounds(
                SyntheticHitResolution.SyntheticHits(strike.WoundCount)));

            return metadata;
        }

        /// <summary>
        /// Offer and roll any move-end strike for <paramref name="mover"/>, which has just finished a
        /// chosen move. Returns null when nothing fired (no ability, no target in range, declined, or
        /// zero successes rolled - the cost IS paid on a fruitless accepted roll, matching every other
        /// once-per-X ability). A non-null result still carries the target so the calling stage can run
        /// the wound pipeline.
        /// </summary>
        public static async Task<StrikeResult?> OfferAndRoll(IGameContext gameContext,
            DataBinding<UnitData> mover)
        {
            IUnit unit = mover.GetValue();
            if (!unit.GetIsAlive())
            {
                return null;
            }

            // Only auto-wound abilities: the strike family is defined by its effect type, the same
            // isolation CrossingAttackStage applies at the move-through hook.
            IReadOnlyList<AbilityOffer> offers = gameContext.RuleEvaluator
                .GatherOffers(new MoveResolvedContext(unit))
                .Where(offer => offer.Ability.Effect is Effect.DealAutoWounds)
                .ToList();

            foreach (AbilityOffer offer in offers)
            {
                List<DataBinding<UnitData>> targets = AbilityTargeting.EligibleTargets(
                    mover, offer.Ability.TargetSelector, gameContext);
                if (targets.Count == 0)
                {
                    continue;
                }

                // Optional by nature: the pick request's cancel IS the decline, so one prompt covers
                // both "use it?" and "on whom?". The target may be any eligible enemy, not just the
                // melee opponent (the Discord-confirmed reading).
                var request = new CancellableSelectionRequest<UnitData>(unit.PlayerID,
                    $"{offer.RuleName}: {unit.Name} may strike one enemy within " +
                    $"{offer.Ability.TargetSelector.RangeInches:0.#}\" (6+ per die deals an unsaveable " +
                    "wound). Pick a target, or cancel to decline.",
                    targets.Select(t => new CancellableSelectionRequest<UnitData>.ValidOption(
                        t, t.GetValue().Name)).ToList(),
                    new List<CancellableSelectionRequest<UnitData>.InvalidOption>(),
                    displayName: $"Choosing {offer.RuleName} Target");

                CancellableResult<DataBinding<UnitData>> picked = await gameContext.PlayerRequester
                    .RequestDecision<CancellableSelectionRequest<UnitData>,
                        CancellableResult<DataBinding<UnitData>>>(request);

                if (picked is not Selected<DataBinding<UnitData>> selected)
                {
                    continue;
                }

                DataBinding<UnitData> enemy = selected.Value;
                IReadOnlyList<RuleOperation> ops = gameContext.RuleEvaluator
                    .ResolveAbility(offer, new[] { (IUnit)enemy.GetValue() });

                OperationApplier.ApplyTokenOperations(ops);
                await OperationExecutor.Execute(ops, new GameOperationServices(gameContext));

                RuleOperation.InvokeDealAutoWounds? autoWounds =
                    ops.OfType<RuleOperation.InvokeDealAutoWounds>().FirstOrDefault();
                if (autoWounds == null || autoWounds.DiceCount <= 0)
                {
                    continue;
                }

                IDiceResults wounds = await SyntheticWoundResolution.RollWoundPool(gameContext,
                    autoWounds.DiceCount, autoWounds.SuccessThreshold, offer.RuleName,
                    enemy.GetValue().Name);

                gameContext.Log($"{unit.Name} used {offer.RuleName}, dealing {wounds.TotalRolls:0.##} " +
                    $"unsaveable wound(s) to {enemy.GetValue().Name} as it ended its move.");

                if (wounds.TotalRolls > 0f)
                {
                    return new StrikeResult(enemy, offer.RuleName, wounds.TotalRolls);
                }
            }

            return null;
        }
    }
}
