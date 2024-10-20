using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Indirect : SpecialRule_Weapon,
        ICombatEffect<OcclusionCheckResults>, ICombatEffect<DetermineHitRollNeededResults>
    {
        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<OcclusionCheckResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetadata metadata, OcclusionCheckResults result)
        {
            throw new System.NotImplementedException();
        }

        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<DetermineHitRollNeededResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetadata metadata, DetermineHitRollNeededResults result)
        {
            throw new System.NotImplementedException();
        }
    }
}
