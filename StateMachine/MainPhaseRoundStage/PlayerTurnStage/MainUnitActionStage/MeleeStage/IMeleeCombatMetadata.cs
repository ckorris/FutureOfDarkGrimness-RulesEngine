
using FFmpeg.AutoGen;
using System.Collections.Generic;

namespace FDG
{
    public interface IMeleeCombatMetadata : ICombatMetadata
    {
        //TODO: Register wounds dealt by each side?
    }

    public class MeleeCombatMetadata : IMeleeCombatMetadata
    {
        public IGameContext GameContext { get; private set; }

        public IWeapon WeaponType { get; private set; }

        public int WeaponCount { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IUnit DefendingUnit { get; private set; }

        public EAttackType AttackType => EAttackType.Melee;

        public IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; private set; }


        private QueryableResults _queryableResults = new QueryableResults();

        public MeleeCombatMetadata(IGameContext gameContext, IUnit attackingUnit, IUnit defendingUnit, IWeapon weaponType, int weaponCount)
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

        private List<ISpecialRule_Combat> GetAllSpecialRules(IUnit attackingUnit, IUnit defendingUnit, IWeapon weaponType)
        {
            List<ISpecialRule_Combat> specialRules = new List<ISpecialRule_Combat>();

            specialRules.AddRange(attackingUnit.SpecialRules);
            //specialRules.AddRange(defendingUnit.SpecialRules); //TODO: Need to differentiate attacker and defender.
            specialRules.AddRange(weaponType.SpecialRules); //TODO: Sometimes the number of weapons matters.

            return specialRules;
        }
    }
}

