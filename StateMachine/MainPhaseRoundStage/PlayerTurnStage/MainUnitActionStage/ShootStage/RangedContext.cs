using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FDG.Stages
{

    public interface IRangedContext : IGameContextAccessor
    {
        public IUnit AttackingUnit { get; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons { get; }

        public IReadOnlyList<IUnit> AvailableTargetUnits { get; }

        public IRangedCombatMetadata RangedCombatMetadata { get; }

        public void BeginNewAttack(IUnit attackingUnit, List<IUnit> availableTargetUnits);

        public void ChooseWeapon(IWeapon weaponToConsume, out int weaponCount);

        public void ChooseTargetUnit(IUnit targetUnit);

        public void ClearCurrentAttack();

        public void ResetRangedCombatMetadata();
    }

    public class RangedContext : IRangedContext
    {
        public IGameContext GameContext { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons => _availableWeapons;

        public IReadOnlyList<IUnit> AvailableTargetUnits { get; private set; }

        public IRangedCombatMetadata RangedCombatMetadata { get; private set; }

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
            RangedCombatMetadata = new RangedCombatMetadata(attackingUnit, GameContext.DiceRoller, 
                GameContext.TextOutput);
        }

        public void ChooseWeapon(IWeapon weaponToConsume, out int weaponCount)
        {
            if(_availableWeapons.ContainsKey(weaponToConsume) == false)
            {
                throw new ArgumentException($"{nameof(RangedContext)}.{nameof(ChooseWeapon)} called on weapon " + 
                    $"that was not found in available list: {weaponToConsume.Name}");
            }

            _availableWeapons.TryRemove(weaponToConsume, out weaponCount);

            RangedCombatMetadata.ChooseWeapon(weaponToConsume, weaponCount);
        }

        public void ChooseTargetUnit(IUnit targetUnit)
        {
            if(AvailableTargetUnits.Contains(targetUnit) == false)
            {
                throw new ArgumentException($"{nameof(RangedContext)}.{nameof(ChooseTargetUnit)} called on unit " +
                    $"that was not found in available list: {targetUnit.Name}");
            }

            RangedCombatMetadata.ChooseTarget(targetUnit);
        }

        public void ClearCurrentAttack()
        {
            AttackingUnit = null;
            AvailableTargetUnits = null;
            _availableWeapons = null;
            RangedCombatMetadata = default;
        }

        public void ResetRangedCombatMetadata()
        {
            RangedCombatMetadata = new RangedCombatMetadata(AttackingUnit, GameContext.DiceRoller, GameContext.TextOutput);
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
    }
}
