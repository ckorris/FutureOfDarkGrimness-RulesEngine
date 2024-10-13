
using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public class RollToHitStage : CombatStage<RollToHitResults, RollToHitStage>
    {
        public RollToHitStage(StateMachine stateMachine, ISingleRangedAttackContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetaData metaData, Action<RollToHitResults> onFinished)
        {
            //TODO: Calculate attack count in separate stage, it may need its own mods.
            float attacks = metaData.WeaponType.Attacks * metaData.WeaponCount;

            IDiceResults rollToHitResults = metaData.DiceRoller.Roll(attacks);

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

            metaData.TextOutput.Log($"Rolled {successfulResults.TotalRolls} successful hits out of {attacks} total attacks.");

            onFinished(results);
        }
    }

    
}