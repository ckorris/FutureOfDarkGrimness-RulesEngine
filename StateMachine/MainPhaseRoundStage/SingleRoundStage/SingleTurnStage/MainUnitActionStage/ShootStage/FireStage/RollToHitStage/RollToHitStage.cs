using System;
using System.Collections.Generic;
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

        protected override async Task RunStage(ICombatMetadata metaData, Action<RollToHitResults> onFinished)
        {
            //TODO: Calculate attack count in separate stage, it may need its own mods.
            float attacks = metaData.WeaponType.Attacks * metaData.WeaponCount;

            IDiceResults rollToHitResults = GameContext.DiceRoller.Roll(attacks);

            //We do this here because modifiers shouldn't do it, or else they can't add up in opposite
            //directions. For example, if your Quality is 6, and something gives you +1 to hit, and something
            //else gives you -1 to hit. They should cancel each other out and you'd need a 6. But if you processed
            //the +1 first, and clamped it, it would still be 6, and the -1 would move it to 5. 

            DetermineHitRollNeededResults hitRollResults = QueryForResultOrThrowException<DetermineHitRollNeededResults>(metaData);

            int hitRollNeeded = DiceUtilities.ClampSuccessRollNeeded(hitRollResults.HitRollNeeded);

            IDiceResults successfulResults = rollToHitResults.SubsetAtOrAbove(hitRollNeeded);
            IDiceResults failedResults = rollToHitResults.SubsetBelow(hitRollNeeded);

            //We only add one to the list for now, but effects might copy and move around.
            RollToHitResults results = new RollToHitResults(
                new List<SuccessfulHitInfo>() { new SuccessfulHitInfo(successfulResults) },
                new List<FailedHitInfo>() { new FailedHitInfo(failedResults) });

            GameContext.Log($"Rolled {successfulResults.TotalRolls} successful hits out of {attacks} total attacks.");

            // #042 extra-hit rules (Surge / Furious / Relentless) fire at hit-roll-complete: an
            // unmodified 6 spawns extra hits. Evaluate the attacker's rules against the UNMODIFIED
            // rolls, fold InsertExtraHits ops through the sink, and append the total as synthetic
            // successes. The stage interprets no operation; generated hits are terminal (not re-fed).
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical: true);

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new HitRollCompleteContext(attacker, defender, rollToHitResults, distance, metaData.IsMelee),
                (attacker, ERuleSeat.Actor));
            HitInjectionSink hitInjection = new HitInjectionSink();
            hitInjection.ApplyFrom(operations);

            if (hitInjection.TotalExtraHits > 0f)
            {
                results.SuccessfulHitList.Add(new SuccessfulHitInfo(SyntheticHits(hitInjection.TotalExtraHits, rollToHitResults)));
            }

            onFinished(results);
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