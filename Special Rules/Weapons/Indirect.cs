using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Indirect : SpecialRule_Weapon,
        ICombatEffect<OcclusionCheckResults>, ICombatEffect<DetermineHitRollNeededResults>
    {
        public void OnPreExecute(ICombatMetaData metadata, ICombatEffectsSink<OcclusionCheckResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetaData metadata, OcclusionCheckResults result)
        {
            throw new System.NotImplementedException();
        }

        public void OnPreExecute(ICombatMetaData metadata, ICombatEffectsSink<DetermineHitRollNeededResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetaData metadata, DetermineHitRollNeededResults result)
        {
            throw new System.NotImplementedException();
        }
    }
}
