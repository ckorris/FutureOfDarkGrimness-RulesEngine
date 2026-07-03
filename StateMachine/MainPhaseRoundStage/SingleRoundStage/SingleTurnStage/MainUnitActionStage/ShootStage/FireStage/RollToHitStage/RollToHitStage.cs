using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{

    public class RollToHitStage<TMetadata> : CombatStage<RollToHitResults, RollToHitStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public RollToHitStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<RollToHitResults, Task> onFinished)
        {
            // Show the attack — tracers (ranged) or a clash (melee) — before the dice resolve it.
            // Fire from the actual weapon-carrying models so a mixed unit shows the right source.
            List<Position> attackerPositions = AttackBeatPositions.FiringModels(metaData.AttackingUnit, metaData.WeaponType);
            // Ranged: only models a shooter can actually see, so tracers spread across the targetable
            // defenders without ever depicting a shot at an unhittable one. Melee is adjacent — LoS is
            // moot and the clash just snaps to the nearest model.
            List<Position> targetPositions = metaData.IsMelee
                ? AttackBeatPositions.AlivePlaced(metaData.DefendingUnit)
                : AttackBeatPositions.VisibleTargets(GameContext.TableState,
                    metaData.AttackingUnit, metaData.DefendingUnit, metaData.WeaponType);
            if (attackerPositions.Count > 0 && targetPositions.Count > 0)
            {
                // Each volley fires every weapon at once; the weapon's Attacks is the volley count.
                await GameContext.Presenter.Present(new AttackBeat(metaData.IsMelee,
                    attackerPositions, targetPositions,
                    volleyCount: metaData.WeaponType.Attacks,
                    armorPenetration: metaData.WeaponType.ArmorPenetration));
            }

            // Attack count and hit threshold are both determined in DetermineHitRollStage (#015).
            // Read them here and roll the determined AttackCount — not a local product — so any
            // attack-count modifier that stage folds in is honoured.
            DetermineHitRollResults hitRollResults = QueryForResultOrThrowException<DetermineHitRollResults>(metaData);

            float attacks = hitRollResults.AttackCount;
            IDiceResults rollToHitResults = GameContext.DiceRoller.Roll(attacks);

            //We do this here because modifiers shouldn't do it, or else they can't add up in opposite
            //directions. For example, if your Quality is 6, and something gives you +1 to hit, and something
            //else gives you -1 to hit. They should cancel each other out and you'd need a 6. But if you processed
            //the +1 first, and clamped it, it would still be 6, and the -1 would move it to 5.

            int hitRollNeeded = DiceUtilities.ClampSuccessRollNeeded(hitRollResults.HitRollNeeded);

            IDiceResults successfulResults = rollToHitResults.SubsetAtOrAbove(hitRollNeeded);
            IDiceResults failedResults = rollToHitResults.SubsetBelow(hitRollNeeded);

            //We only add one to the list for now, but effects might copy and move around.
            RollToHitResults results = new RollToHitResults(
                new List<SuccessfulHitInfo>() { new SuccessfulHitInfo(successfulResults) },
                new List<FailedHitInfo>() { new FailedHitInfo(failedResults) });

            GameContext.Log($"Rolled {successfulResults.TotalRolls} successful hits out of {attacks} total attacks.");

            // Show the natural to-hit roll (the synthetic extra-hits below aren't dice).
            await GameContext.Presenter.Present(
                DiceRolledBeat.From(rollToHitResults, hitRollNeeded, GameContext.Settings.RandomnessType, "Roll to Hit",
                    $"{successfulResults.TotalRolls:0.##} hits"));

            // #042 extra-hit rules (Surge / Furious / Relentless) fire at hit-roll-complete: an
            // unmodified 6 spawns extra hits. Evaluate the attacker's rules against the UNMODIFIED
            // rolls, fold InsertExtraHits ops through the sink, and append the total as synthetic
            // successes. The stage interprets no operation; generated hits are terminal (not re-fed).
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical: true);

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new HitRollCompleteContext(attacker, defender, rollToHitResults, distance, metaData.IsMelee,
                    metaData.IsCharging),
                // #006 slice F / #093: the attacker batch's living owners contribute their per-model rules
                // under AllOwners semantics (fires only when every owner shares it), so a joined hero's
                // Furious/Relentless fire for a hero-only batch and a homogeneous squad's shared per-model
                // rule fires once — without leaking onto a mixed batch's pooled roll.
                (attacker, ERuleSeat.Actor, metaData.WeaponType,
                    HeroStatRules.LivingWeaponBatchOwners(metaData.AttackingUnit.GetValue(), metaData.WeaponType),
                    EModelRuleScope.AllOwners),
                // The defender contributes its DEFENSIVE save modifiers here (Shielded's +1 to defense) —
                // the mirror of how DetermineHitRollStage evaluates the defender as Subject for hit
                // modifiers. Its Net(Save) folds into RollToHitResults.SaveModifier below alongside the
                // attacker's AP, so a defensive +1 and an attacker's -N net correctly.
                (defender, ERuleSeat.Subject, (IWeapon?)null, (IReadOnlyList<IModel>?)null,
                    EModelRuleScope.AnyOwner));
            HitInjectionSink hitInjection = new HitInjectionSink();
            hitInjection.ApplyFrom(operations);

            if (hitInjection.TotalExtraHits > 0f)
            {
                results.SuccessfulHitList.Add(new SuccessfulHitInfo(SyntheticHits(hitInjection.TotalExtraHits, rollToHitResults)));
            }

            // #042 hit-multiplier rules (Blast) fire at the same hook but resolve "after other rules":
            // multiply the POST-injection hit total, then cap at the target unit's model count. Folded
            // after the injection above so Blast multiplies whatever hits landed (including Surge's).
            HitMultiplierSink hitMultiplier = new HitMultiplierSink();
            hitMultiplier.ApplyFrom(operations);
            if (hitMultiplier.NetMultiplier > 1)
            {
                float currentHits = TotalHits(results);
                int targetModelCount = CountLivingModels(defender);
                float cappedHits = Math.Min(currentHits * hitMultiplier.NetMultiplier, targetModelCount);
                float extraHits = cappedHits - currentHits;
                if (extraHits > 0f)
                {
                    results.SuccessfulHitList.Add(new SuccessfulHitInfo(SyntheticHits(extraHits, rollToHitResults)));
                    GameContext.Log($"Blast multiplied {currentHits} hits x{hitMultiplier.NetMultiplier}, capped at " +
                        $"{targetModelCount} target models -> {cappedHits} total.");
                }
            }

            // #042 save-modifier rules (Rending) also fire at hit-roll-complete: an unmodified 6 to hit
            // promotes the attack's AP, modelled as a save-roll modifier on the defender. Fold it here —
            // where the UNMODIFIED roll is still correct (synthetic hits sit at face 6 and would pollute
            // a later read) — and carry the scalar to the save stage via RollToHitResults.SaveModifier.
            RollModifierSink saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(operations);
            results.SaveModifier = saveModifiers.Net(ERollKind.Save);

            // Fortified (defender) reduces the incoming WEAPON AP, floored at the save stage. Sum any
            // reduction ops here and carry the total alongside the save modifier.
            results.ArmorPenetrationReduction = operations
                .OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

            await onFinished(results);
        }

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList)
            {
                total += hit.HitCount;
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

        // Bridges a scalar extra-hit count into the IDiceResults the save flow consumes. Injected
        // hits have no real face — only the count (TotalRolls) matters downstream, plus the weapon's
        // AP — so they sit at the top face as automatic successes. If a future per-hit rule (Rending,
        // #032) reads a hit's face, it must treat injected hits as base-AP, not natural 6s.
        private static IDiceResults SyntheticHits(float count, IDiceResults template)
        {
            int faceCount = template.SideMax - template.SideMin + 1;
            float[] perSide = new float[faceCount];
            perSide[faceCount - 1] = count;
            return new DiceResults(perSide, template.SideMin);
        }
    }

    
}