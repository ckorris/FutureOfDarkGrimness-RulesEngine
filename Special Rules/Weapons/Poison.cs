using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    [System.Serializable]
    public class Poison : SpecialRule_Weapon, ICombatEffect<RollToSaveResults>
    {
        private const int REROLL_SAVE_VALUE = 6;

        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<RollToSaveResults> sink)
        {
            //Remove regeneration.

            List<ICombatEffect<RollToSaveResults>> allSinkEffects = sink.OnExecuteEffectsList; //Shorthand.

            List<Regeneration> allRegenEffects =
                new List<Regeneration>(allSinkEffects.OfType<Regeneration>().ToList());

            if (allRegenEffects.Count > 0)
            {
                foreach (Regeneration regenEffect in allRegenEffects)
                {
                    allSinkEffects.Remove(regenEffect);
                }
                metadata.TextOutput().Log($"Poison removed {nameof(Regeneration)} effect.");
            }
        }

        public void OnPostExecute(ICombatMetadata metadata, RollToSaveResults result)
        {
            float totalNeededToReroll = 0;
            float totalRerolledAndFailed = 0;

            foreach (SuccessfulSaveInfo originalSuccesses in new List<SuccessfulSaveInfo>(result.SuccessfulSaveList))
            {
                //Remove any saves that were made with a 6.
                IDiceResults needToReroll = originalSuccesses.Rolls.SubsetAtOrAbove(REROLL_SAVE_VALUE);
                IDiceResults dontNeedToReroll = originalSuccesses.Rolls.SubsetBelow(REROLL_SAVE_VALUE);

                totalNeededToReroll += needToReroll.TotalRolls;

                result.SuccessfulSaveList.Remove(originalSuccesses);

                //Replace the one we removed with the ones that didn't need to be rerolled.
                result.SuccessfulSaveList.Add(new SuccessfulSaveInfo(dontNeedToReroll, originalSuccesses.RollNeededInfo));

                //Reroll them.
                IDiceResults rerolls = metadata.DiceRoller().Roll(needToReroll.TotalRolls);

                int saveNeeded = originalSuccesses.RollNeededInfo.SaveNeeded;
                IDiceResults successfulRerolls = rerolls.SubsetAtOrAbove(saveNeeded);
                IDiceResults failedRerolls = rerolls.SubsetBelow(saveNeeded);

                totalRerolledAndFailed += failedRerolls.TotalRolls;

                result.SuccessfulSaveList.Add(new SuccessfulSaveInfo(successfulRerolls, originalSuccesses.RollNeededInfo));
                result.FailedSaveList.Add(new FailedSaveInfo(failedRerolls, originalSuccesses.RollNeededInfo));
            }

            metadata.TextOutput().Log($"Poison forced rerolls of {totalNeededToReroll} dice, and {totalRerolledAndFailed} failed.");
        }
    }
}