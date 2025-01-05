using System.Collections.Generic;

namespace FDG
{
    public interface ICombatMetadata
    {
        public IGameContext GameContext { get; }

        public IWeapon WeaponType { get; }

        public int WeaponCount { get; }

        public IUnit AttackingUnit { get; }

        public IUnit DefendingUnit { get; }

        IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; }

        //TODO: Next value can replace everything after?
        public void AddResult<TResult>(TResult result);

        public bool QueryForResult<TResult>(out TResult result);
    }

    public class CombatMetadata : ICombatMetadata
    {
        public IGameContext GameContext { get; private set; }

        public IWeapon WeaponType { get; private set; }

        public int WeaponCount { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IUnit DefendingUnit { get; private set; }


        public IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; private set; }


        private QueryableResults _queryableResults = new QueryableResults();

        public CombatMetadata(IGameContext gameContext, IUnit attackingUnit,
            IUnit defendingUnit, IWeapon weaponType, int weaponCount)
        {
            GameContext = gameContext;
            AttackingUnit = attackingUnit;
            DefendingUnit = defendingUnit;
            WeaponType = weaponType;
            WeaponCount = weaponCount;

            AllSpecialRules = GetAllSpecialRules(attackingUnit, defendingUnit, weaponType);
        }

        public void AddResult<TResult>(TResult result)
        {
            _queryableResults.AddResult(result);
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            return _queryableResults.QueryForResult(out result);
        }

        private List<ISpecialRule_Combat> GetAllSpecialRules(IUnit attackingUnit, IUnit defendingUnit,
            IWeapon weaponType)
        {
            List<ISpecialRule_Combat> specialRules = new List<ISpecialRule_Combat>();

            specialRules.AddRange(attackingUnit.GetAttackerSpecialRules());
            specialRules.AddRange(defendingUnit.GetDefenderSpecialRules()); //TODO: Need to differentiate attacker and defender.
            specialRules.AddRange(weaponType.SpecialRules); //TODO: Sometimes the number of weapons matters.

            return specialRules;
        }
    }

    public static class ICombatMetaDataExtensions
    {
        public static IDiceRoller DiceRoller(this ICombatMetadata metadata)
        {
            return metadata.GameContext.DiceRoller;
        }

        public static ITextOutput TextOutput(this ICombatMetadata metadata)
        {
            return metadata.GameContext.TextOutput;
        }
    }
}