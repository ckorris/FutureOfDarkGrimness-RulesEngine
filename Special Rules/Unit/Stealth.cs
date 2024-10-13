using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Stealth : ISpecialRule_Defender, ICombatEffect<DetermineHitRollNeededResults>
    {
        public List<ICombatEffect<TResult>> GetEffects<TResult>()
        {
            return this.GetEffectsListFromOwnType<TResult>();
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