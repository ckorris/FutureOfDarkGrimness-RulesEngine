
using System;
using System.Collections.Generic;
using FDG.Presentation;
using FDG.Presentation.Beats;

namespace FDG.Stages
{
    public class RollToSaveStage<TMetadata> : CombatStage<RollToSaveResults, RollToSaveStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public RollToSaveStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Action<RollToSaveResults> onFinished)
        {
            List<SuccessfulSaveInfo> successfulSaves = new List<SuccessfulSaveInfo>();
            List<FailedSaveInfo> failedSaves = new List<FailedSaveInfo>();

            float totalSuccesses = 0;
            float totalFailures = 0;

            DetermineSaveRollNeededResults saveRollsNeeded = QueryForResultOrThrowException<DetermineSaveRollNeededResults>(metaData);

            foreach (PendingSaveRolls saveRolls in saveRollsNeeded.PendingSaveRollsList)
            {
                IDiceResults rollToSaveResults = GameContext.DiceRoller.Roll(saveRolls.HitCount);

                int saveNeeded = DiceUtilities.ClampSuccessRollNeeded(saveRolls.SaveNeeded);

                IDiceResults successfulResults = rollToSaveResults.SubsetAtOrAbove(saveNeeded);
                IDiceResults failedResults = rollToSaveResults.SubsetBelow(saveNeeded);

                successfulSaves.Add(new SuccessfulSaveInfo(successfulResults, saveRolls));
                failedSaves.Add(new FailedSaveInfo(failedResults, saveRolls));

                totalSuccesses += successfulResults.TotalRolls;
                totalFailures += failedResults.TotalRolls;

                await GameContext.Presenter.Present(
                    DiceRolledBeat.From(rollToSaveResults, saveNeeded, GameContext.Settings.RandomnessType, "To Save"));
            }

            RollToSaveResults results = new RollToSaveResults(successfulSaves, failedSaves);

            GameContext.Log($"Saved {totalSuccesses} wounds, taking {totalFailures}.");

            onFinished(results);
        }
    }
}