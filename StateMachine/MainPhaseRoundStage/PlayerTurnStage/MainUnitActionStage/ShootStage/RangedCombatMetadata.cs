
using System.Collections.Generic;

namespace FDG
{

    public interface IRangedCombatMetadata : ICombatMetadata
    {
        public void ChooseTarget(IUnit targetUnit);
    }

    public class RangedCombatMetadata : IRangedCombatMetadata
    {
        public IGameContext GameContext { get; private set; }

        public IWeapon WeaponType { get; private set; }

        public int WeaponCount { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IUnit DefendingUnit { get; private set; }

        public EAttackType AttackType => EAttackType.Ranged;

        public IReadOnlyList<ISpecialRule_Combat> AllSpecialRules => throw new System.NotImplementedException();

        private bool _hasSetWeapon = false;
        private bool _hasSetTargetUnit = false;

        private QueryableResults _queryableResults = new QueryableResults();

        public RangedCombatMetadata(IGameContext gameContext, IUnit attackingUnit, IUnit defendingUnit, IWeapon weaponType, int weaponCount)
        {
            GameContext = gameContext;
            AttackingUnit = attackingUnit;
            DefendingUnit = defendingUnit;
            WeaponType = weaponType;
            WeaponCount = weaponCount;
        }

        public void AddResult<TResult>(TResult result)
        {
            _queryableResults.AddResult(result);
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            return _queryableResults.QueryForResult(out result);
        }

        public void ChooseWeapon(IWeapon weaponType, int weaponCount)
        {
            WeaponType = weaponType;
            WeaponCount = weaponCount;

            _hasSetWeapon = true;
        }

        public void ChooseTarget(IUnit targetUnit)
        {
            DefendingUnit = targetUnit;

            _hasSetTargetUnit = true;
        }
    }
}