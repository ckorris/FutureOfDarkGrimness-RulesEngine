
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

        protected override async Task RunStage(ICombatMetadata metaData, Func<RollToSaveResults, Task> onFinished)
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

                // RollToHitStage emits one hit group per volley even when it whiffs (0 hits), so a missed
                // volley reaches here as a group with no save dice. Don't narrate a hollow "0 saved,
                // 0 wounds" roll-to-save animation for it.
                if (saveRolls.HitCount > 0)
                {
                    await GameContext.Presenter.Present(
                        DiceRolledBeat.From(rollToSaveResults, saveNeeded, GameContext.Settings.RandomnessType, "Roll to Save",
                            $"{successfulResults.TotalRolls:0.##} saved, {failedResults.TotalRolls:0.##} wounds"));
                }
            }

            RollToSaveResults results = new RollToSaveResults(successfulSaves, failedSaves);

            GameContext.Log($"Saved {totalSuccesses} wounds, taking {totalFailures}.");

            // Deflection "pings" for the saved shots. Saves are resolved per AP group, not per
            // defending model, so this is unit-level (a count across the defender's models).
            int savedCount = (int)MathF.Round(totalSuccesses);
            if (savedCount > 0)
            {
                List<Position> defenders = AttackBeatPositions.AlivePlaced(metaData.DefendingUnit);
                if (defenders.Count > 0)
                    await GameContext.Presenter.Present(new SaveBeat(defenders, savedCount));
            }

            await onFinished(results);
        }
    }
}