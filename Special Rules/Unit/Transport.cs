using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Transport : SpecialRule //Not yet sure how this will be implemented as it's quite unique.
    {
        public List<ICombatEffect<TResult>> GetEffects<TResult>()
        {
            throw new System.NotImplementedException();
        }
    }
}