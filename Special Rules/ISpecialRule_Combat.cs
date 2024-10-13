using System.Collections.Generic;

namespace FDG
{
    public interface ISpecialRule_Combat : ISpecialRule //TODO: Do we need this parent one?
    {
        public List<ICombatEffect<TResult>> GetEffects<TResult>();
    }

    public class SpecialRule_Combat : ISpecialRule_Combat
    {
        public List<ICombatEffect<TResult>> GetEffects<TResult>()
        {
            return this.GetEffectsListFromOwnType<TResult>();
        }
    }

    public static class ISpecialRuleExtensions
    {
        /// <summary>
        /// Returns a list that has <paramref name="rule"/> as the converted type, if it's implementing
        /// the expected type itself. You can use this in classes that implement both <see cref="ISpecialRule_Combat"/>
        /// and also the <see cref="ICombatEffect{TResult}"/> that they should return.
        /// </summary><remarks>
        /// This was made to make it easier to implement <see cref="ISpecialRule_Combat.GetEffects{TResult}"/> on 
        /// effects that have the logic themselves.
        /// </remarks>
        public static List<ICombatEffect<TQueryResult>> GetEffectsListFromOwnType<TQueryResult>(this ISpecialRule_Combat rule)
        {
            if (rule is ICombatEffect<TQueryResult> asResultType)
            {
                return new List<ICombatEffect<TQueryResult>>() { asResultType };
            }

            return new List<ICombatEffect<TQueryResult>>();
        }
    }
}