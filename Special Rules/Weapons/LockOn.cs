using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class LockOn : SpecialRule_Weapon, ICombatEffect<DetermineHitRollNeededResults>, ICombatEffect<RangeCheckResults>
    {
        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<DetermineHitRollNeededResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetadata metadata, DetermineHitRollNeededResults result)
        {
            throw new System.NotImplementedException();
        }

        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<RangeCheckResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetadata metadata, RangeCheckResults result)
        {
            throw new System.NotImplementedException();
        }
    }
}