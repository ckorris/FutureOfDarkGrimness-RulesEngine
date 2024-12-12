using System.Collections.Generic;

namespace FDG
{

    [System.Serializable]
    public class Regeneration : ISpecialRule_Defender, ICombatEffect<RollToSaveResults>
    {
        private const int ROLL_NEEDED_TO_REGENERATE = 5; //TODO: Might some effects have to change this?

        public List<ICombatEffect<TResult>> GetEffects<TResult>()
        {
            return this.GetEffectsListFromOwnType<TResult>();
        }

        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<RollToSaveResults> sink) { }

        public void OnPostExecute(ICombatMetadata metadata, RollToSaveResults result)
        {
            //For all failed saves, roll another die, and make it a successful save if high enough.

            foreach (FailedSaveInfo failedSave in new List<FailedSaveInfo>(result.FailedSaveList))
            {
                IDiceResults regenAttempts = metadata.DiceRoller().Roll(failedSave.SaveCount);

                IDiceResults successfulRegens = regenAttempts.SubsetAtOrAbove(ROLL_NEEDED_TO_REGENERATE);

                SuccessfulSaveInfo successfulSaveInfo = new SuccessfulSaveInfo(successfulRegens, failedSave.RollNeededInfo);

                metadata.TextOutput().Log($"{nameof(Regeneration)} converted {successfulRegens.TotalRolls} failed saves into saves");

                result.FailedSaveList.Remove(failedSave);
                result.SuccessfulSaveList.Add(successfulSaveInfo);
            }
        }
    }
}