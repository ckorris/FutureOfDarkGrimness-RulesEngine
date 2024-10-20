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