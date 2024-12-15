
using System.Collections.Generic;

namespace FDG
{
    public interface ISpecialRule_Weapon : ISpecialRule_Combat
    {

    }

    public class SpecialRule_Weapon : ISpecialRule_Weapon
    {
        public List<ICombatEffect<TResult>> GetEffects<TResult>()
        {
            return this.GetEffectsListFromOwnType<TResult>();
        }
    }
}