
using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class RollToSaveStage : CombatStage<RollToSaveResults, RollToSaveStage, ICombatMetadata>
    {
        public RollToSaveStage(StateMachine stateMachine, ISingleAttackContext<ICombatMetadata> context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<RollToSaveResults> onFinished)
        {
            List<SuccessfulSaveInfo> successfulSaves = new List<SuccessfulSaveInfo>();
            List<FailedSaveInfo> failedSaves = new List<FailedSaveInfo>();

            float totalSuccesses = 0;
            float totalFailures = 0;

            DetermineSaveRollNeededResults saveRollsNeeded = QueryForResultOrThrowException<DetermineSaveRollNeededResults>(metaData);

            foreach (PendingSaveRolls saveRolls in saveRollsNeeded.PendingSaveRollsList)
            {
                IDiceResults rollToSaveResults = metaData.DiceRoller.Roll(saveRolls.HitCount);

                int saveNeeded = DiceUtilities.ClampSuccessRollNeeded(saveRolls.SaveNeeded);

                IDiceResults successfulResults = rollToSaveResults.SubsetAtOrAbove(saveNeeded);
                IDiceResults failedResults = rollToSaveResults.SubsetBelow(saveNeeded);

                successfulSaves.Add(new SuccessfulSaveInfo(successfulResults, saveRolls));
                failedSaves.Add(new FailedSaveInfo(failedResults, saveRolls));

                totalSuccesses += successfulResults.TotalRolls;
                totalFailures += failedResults.TotalRolls;
            }

            RollToSaveResults results = new RollToSaveResults(successfulSaves, failedSaves);

            metaData.TextOutput.Log($"Saved {totalSuccesses} wounds, taking {totalFailures}.");

            onFinished(results);
        }
    }
}