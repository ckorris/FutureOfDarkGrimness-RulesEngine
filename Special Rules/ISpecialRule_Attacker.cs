
using System.Collections.Generic;

namespace FDG
{
    public interface ISpecialRule_Attacker : ISpecialRule_Combat
    {

    }

    public class SpecialRule_Attacker : ISpecialRule_Attacker
    {
        public List<ICombatEffect<TResult>> GetEffects<TResult>()
        {
            return this.GetEffectsListFromOwnType<TResult>();
        }
    }
}