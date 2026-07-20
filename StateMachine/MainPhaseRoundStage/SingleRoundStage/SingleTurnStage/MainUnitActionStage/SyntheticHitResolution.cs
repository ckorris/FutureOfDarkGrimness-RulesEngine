using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #164 — resolves a batch of AUTO-HITTING synthetic hits through the shared hit-complete fold, so an
    /// effect that "deals N hits" gets the same weapon-rule treatment a fired volley does: Blast multiplies
    /// (capped at the target's living-model count), "on an unmodified 6" rules add hits, Rending/Crack
    /// promote AP on the natural 6s, and the defender's own save modifiers / AP reduction fold in.
    ///
    /// <para>Three call sites share this: a damage spell (<see cref="ResolveSpellDamageStage"/>), a
    /// pre-attack activated ability (<see cref="PreAttackStage"/> — Breath Attack), and Strafing
    /// (<see cref="StrafingStage"/>). Before this existed only the spell path ran the fold; the other two
    /// synthesized a raw <see cref="RollToHitResults"/> from the bare hit count, so an ability authored as
    /// <c>DealHits(1, with: Blast(3))</c> silently dealt one hit instead of three.</para>
    ///
    /// <para>The dice faces don't GATE the hits — every die is an automatic hit — they only feed the on-6
    /// rules, so the rolled histogram itself is the successful-hit batch the per-hit AP split partitions.</para>
    /// </summary>
    public static class SyntheticHitResolution
    {
        /// <summary>
        /// The folded outcome: the per-AP hit groups the save flow consumes, plus the whole-attack save
        /// modifier and the defender's AP reduction, which ride <see cref="RollToHitResults"/> alongside.
        /// </summary>
        public readonly record struct Result(
            List<SuccessfulHitInfo> HitGroups, int SaveModifier, int ArmorPenetrationReduction);

        /// <summary>
        /// Rolls <paramref name="baseHits"/> as real dice and runs the hit-complete fold (the same machinery
        /// <c>RollToHitStage</c> uses) for <paramref name="attacker"/> against <paramref name="target"/>.
        /// <paramref name="weapon"/> is the synthetic weapon carrying the effect's AP and its resolved
        /// weapon rules. <paramref name="isSpell"/> marks the hits as spell-sourced so rules whose corpus
        /// text excludes spells (Shielded) gate themselves out via <c>Condition.IsNotSpell</c>.
        /// </summary>
        public static Result Resolve(IGameContext gameContext, IUnit attacker, IUnit target,
            int baseHits, Weapon weapon, bool isSpell)
        {
            IDiceResults rolled = gameContext.DiceRoller.Roll(baseHits);
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, target, out _, out _,
                includeVertical: false);

            // The DEFENDER participates too (Subject seat), mirroring RollToHitStage: Fortified's AP
            // reduction fires against synthetic damage like any other hit.
            IReadOnlyList<RuleOperation> ops = gameContext.RuleEvaluator.EvaluateAll(
                new HitRollCompleteContext(attacker, target, rolled, distance, IsMelee: false,
                    IsCharging: false, IsSpell: isSpell),
                RuleParticipant.Actor(attacker, weapon),
                // #183: the target's living models surface a joined hero's relocated defensive rules
                // (Fortified vs spell AP; Shielded self-gates out via IsNotSpell) after the merge.
                RuleParticipant.Subject(target, models: HeroStatRules.LivingModels(target)));

            // Split the auto-hitting rolled dice so a natural 6 carries Rending/Crack AP per-hit while the
            // rest save at base AP. No per-hit rule -> a single base-AP group holding every rolled hit.
            List<SuccessfulHitInfo> groups = PerHitApSplitter.Split(rolled, ops);

            // Surge-style extra hits are base AP (a generated hit is not itself a natural 6).
            HitInjectionSink injection = new HitInjectionSink();
            injection.ApplyFrom(ops);
            if (injection.TotalExtraHits > 0f)
            {
                groups.Add(new SuccessfulHitInfo(SyntheticHits(injection.TotalExtraHits)));
            }

            // Blast multiplies the POST-injection total, capped at the target's living-model count; the
            // added hits are base AP. Mirrors RollToHitStage — the original groups (including the AP group)
            // are untouched and only the capped extra is appended.
            HitMultiplierSink multiplier = new HitMultiplierSink();
            multiplier.ApplyFrom(ops);
            if (multiplier.NetMultiplier > 1)
            {
                float currentHits = TotalHits(groups);
                float cappedHits = System.Math.Min(currentHits * multiplier.NetMultiplier, CountLivingModels(target));
                float extraHits = cappedHits - currentHits;
                if (extraHits > 0f)
                {
                    groups.Add(new SuccessfulHitInfo(SyntheticHits(extraHits)));
                }
            }

            RollModifierSink saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(ops);

            // Fortified (defender) reduces the incoming AP, floored at the save stage — the same fold
            // RollToHitStage does for weapon attacks.
            int apReduction = ops.OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

            return new Result(groups, saveModifiers.Net(ERollKind.Save), apReduction);
        }

        private static float TotalHits(List<SuccessfulHitInfo> groups)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo group in groups)
            {
                total += group.HitCount;
            }
            return total;
        }

        private static int CountLivingModels(IUnit unit)
        {
            int count = 0;
            foreach (IModel model in unit.Models)
            {
                if (model.GetIsAlive()) count++;
            }
            return count;
        }

        /// <summary>
        /// Bridges a scalar hit count into the <see cref="IDiceResults"/> the save flow consumes. The face
        /// is cosmetic — saves count by total.
        /// </summary>
        public static IDiceResults SyntheticHits(float count)
        {
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[perSide.Length - 1] = count;
            return new DiceResults(perSide);
        }
    }
}
