using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

/*
namespace FDG.Stages
{

    public interface ICombatActionContext : IGameContextAccessor
    {
        public IUnit AttackingUnit { get; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons { get; }

        public IReadOnlyList<IUnit> AvailableTargetUnits { get; }

        public ICombatMetadata RangedCombatMetadata { get; }

        public void BeginNewAttack(IUnit defendingUnit);

        public void SetAttackWeapon(IWeapon weaponToConsume, out int weaponCount);

        public ICombatMetadata ConsumeAttackIntoContext(IGameContext gameContext);
    }

    public class RangedContext : ICombatActionContext
    {
        public IGameContext GameContext { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons => _availableWeapons;

        public IReadOnlyList<IUnit> AvailableTargetUnits { get; private set; }

        public ICombatMetadata RangedCombatMetadata { get; private set; }

        private ConcurrentDictionary<IWeapon, int> _availableWeapons;


        public RangedContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }

        public void BeginNewAttack(IUnit attackingUnit, List<IUnit> availableTargetUnits)
        {
            AttackingUnit = attackingUnit;
            AvailableTargetUnits = availableTargetUnits;
            _availableWeapons = GetTypeSortedWeapons(attackingUnit.GetRangedWeapons());

            throw new NotImplementedException();
            //RangedCombatMetadata = new RangedCombatMetadata(GameContext, attackingUnit, );
        }

        public void SetAttackWeapon(IWeapon weaponToConsume, out int weaponCount)
        {
            if(_availableWeapons.ContainsKey(weaponToConsume) == false)
            {
                throw new ArgumentException($"{nameof(RangedContext)}.{nameof(SetAttackWeapon)} called on weapon " + 
                    $"that was not found in available list: {weaponToConsume.Name}");
            }

            _availableWeapons.TryRemove(weaponToConsume, out weaponCount);

            throw new NotImplementedException();
            //RangedCombatMetadata.ChooseWeapon(weaponToConsume, weaponCount);
        }

        public ICombatMetadata ConsumeAttackIntoContext(IGameContext gameContext)
        {
            throw new NotImplementedException();
        }


        //TODO: Repeated in Melee version. Move to static class.
        private ConcurrentDictionary<IWeapon, int> GetTypeSortedWeapons(List<IWeapon> weapons)
        {
            ConcurrentDictionary<IWeapon, int> weaponsAndCounts = new ConcurrentDictionary<IWeapon, int>();

            WeaponComparer comparer = new WeaponComparer();

            foreach(IWeapon newWeapon in weapons)
            {
                IWeapon identicalWeapon = weaponsAndCounts.Keys.FirstOrDefault(keyWeapon => comparer.Equals(newWeapon, keyWeapon));

                if (identicalWeapon != default)
                {
                    weaponsAndCounts[identicalWeapon]++;
                }
                else
                {
                    weaponsAndCounts[newWeapon] = 1;
                }
            }

            return weaponsAndCounts;
        }

        ICombatMetadata ICombatActionContext.ConsumeAttackIntoContext(IGameContext gameContext)
        {
            throw new NotImplementedException();
        }

        private class PendingAttack //TODO: Duplicated from melee.
        {
            public bool IsReady => DefendingUnit != default && WeaponType != default && WeaponCount != default;

            public IUnit DefendingUnit = default;

            public IWeapon WeaponType = default;

            public int WeaponCount = default;
        }

    }
}
*/