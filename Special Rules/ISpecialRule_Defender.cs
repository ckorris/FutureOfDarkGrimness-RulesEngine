
using System.Collections.Generic;

namespace FDG
{
    public interface ISpecialRule_Defender : ISpecialRule_Combat
    {

    }

    public class SpecialRule_Defender : ISpecialRule_Defender
    {
        public List<ICombatEffect<TResult>> GetEffects<TResult>()
        {
            return this.GetEffectsListFromOwnType<TResult>();
        }
    }
}