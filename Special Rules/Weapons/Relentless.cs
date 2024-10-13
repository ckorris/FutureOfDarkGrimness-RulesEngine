
using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Relentless : SpecialRule_Weapon, ICombatEffect<RollToHitResults>
    {
        private const int ROLL_TO_ADD_EXTRA_SHOT = 6;

        public void OnPreExecute(ICombatMetaData metadata, ICombatEffectsSink<RollToHitResults> sink)
        {

        }

        public void OnPostExecute(ICombatMetaData metadata, RollToHitResults result)
        {
            //TODO: Check if the unit has moved. For now, assume it has not.
            /*
            if(false)
            {
                metadata.TextOutput.Log($"{nameof(Relentless)} wasn't applied because the unit moved this turn.");
            }
            */

            float totalUpgraded = 0;

            foreach (SuccessfulHitInfo successfulHit in new List<SuccessfulHitInfo>(result.SuccessfulHitList))
            {
                IDiceResults qualifyForDoubleRolls = successfulHit.Rolls.SubsetAtOrAbove(ROLL_TO_ADD_EXTRA_SHOT);

                totalUpgraded += qualifyForDoubleRolls.TotalRolls;

                result.SuccessfulHitList.Add(new SuccessfulHitInfo(qualifyForDoubleRolls));
            }

            metadata.TextOutput.Log($"{nameof(Relentless)} added {totalUpgraded} hits from rolls of {ROLL_TO_ADD_EXTRA_SHOT}.");
        }
    }
}
