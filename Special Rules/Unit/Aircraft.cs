using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Aircraft : ISpecialRule_Defender,
        ICombatEffect<RangeCheckResults>, ICombatEffect<DetermineHitRollNeededResults>
    {
        public List<ICombatEffect<TResult>> GetEffects<TResult>()
        {
            return this.GetEffectsListFromOwnType<TResult>();
        }

        public void OnPreExecute(ICombatMetaData metadata, ICombatEffectsSink<RangeCheckResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetaData metadata, RangeCheckResults result)
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