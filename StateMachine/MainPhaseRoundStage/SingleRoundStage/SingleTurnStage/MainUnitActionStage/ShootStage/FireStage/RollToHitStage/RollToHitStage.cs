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

            GameContext.Log($"Rolled {successfulResults.TotalRolls} successful hits out of {attacks} total attacks.");

            // Show the natural to-hit roll (the synthetic extra-hits below aren't dice).
            await GameContext.Presenter.Present(
                DiceRolledBeat.From(rollToHitResults, hitRollNeeded, GameContext.Settings.RandomnessType, "Roll to Hit",
                    $"{successfulResults.TotalRolls:0.##} hits"));

            // #042 hit-roll-complete rules fire here, evaluated against the UNMODIFIED rolls (the synthetic
            // hits added below sit at face 6 and would pollute a natural-6 read). One evaluation feeds them
            // all: extra-hit (Surge/Furious/Relentless), hit-multiplier (Blast), per-hit-AP (Rending/Crack)
            // and whole-attack save (Thrust) rules on the attacker, plus the defender's defensive save mods.
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical: true);

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new HitRollCompleteContext(attacker, defender, rollToHitResults, distance, metaData.IsMelee,
                    metaData.IsCharging, IsSpell: false,
                    ChargeOriginDistanceInches: metaData.ChargeOriginDistanceInches),
                // #006 slice F / #093: the attacker batch's living owners contribute their per-model rules
                // under AllOwners semantics (fires only when every owner shares it), so a joined hero's
                // Furious/Relentless fire for a hero-only batch and a homogeneous squad's shared per-model
                // rule fires once — without leaking onto a mixed batch's pooled roll.
                RuleParticipant.Actor(attacker, metaData.WeaponType,
                    HeroStatRules.LivingWeaponBatchOwners(metaData.AttackingUnit.GetValue(), metaData.WeaponType),
                    EModelRuleScope.AllOwners),
                // The defender contributes its DEFENSIVE save modifiers here (Shielded's +1 to defense,
                // Fortified's AP reduction) — the mirror of how DetermineHitRollStage evaluates the defender
                // as Subject for hit modifiers. Its Net(Save) folds into RollToHitResults.SaveModifier below
                // alongside the attacker's whole-attack AP, so a defensive +1 and an attacker's -N net
                // correctly. #183: passing the defender's living models surfaces a joined hero's relocated
                // Shielded/Fortified (gated by AllModelsHaveThisRule) instead of silently dropping them.
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender)));

            // #032 per-hit AP (Rending/Crack on an unmodified 6): split the rolled successes so only the
            // matching-face hits carry the raised save threshold; the rest stay at base AP. With no such
            // rule the splitter returns a single group — identical to the old one-group behaviour.
            RollToHitResults results = new RollToHitResults(
                PerHitApSplitter.Split(successfulResults, operations),
                new List<FailedHitInfo>() { new FailedHitInfo(failedResults) });

            // #042 extra-hit rules (Surge / Furious / Relentless) fire at hit-roll-complete: an
            // unmodified 6 spawns extra hits. Fold InsertExtraHits ops through the sink and append the
            // total as synthetic successes (base AP — a generated hit is not itself a natural 6).
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

            // Whole-attack save-modifier rules fold their net modifier here and carry it to the save stage
            // via RollToHitResults.SaveModifier — Thrust's charge AP, the defender's Shielded +1. Rending
            // and Crack no longer ride this scalar: their AP is per-hit (split into a face group above), so
            // this now holds only genuinely attack-wide modifiers.
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
        // AP — so they sit at the top face as automatic successes. The #032 per-hit AP split
        // (Rending/Crack) runs only over the ROLLED successes, never these synthetic groups, so injected
        // hits stay base-AP even though they sit at face 6 — exactly as they must.
        private static IDiceResults SyntheticHits(float count, IDiceResults template)
        {
            int faceCount = template.SideMax - template.SideMin + 1;
            float[] perSide = new float[faceCount];
            perSide[faceCount - 1] = count;
            return new DiceResults(perSide, template.SideMin);
        }
    }

    
}