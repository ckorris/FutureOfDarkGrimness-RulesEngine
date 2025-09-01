using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using FDG.Data;

namespace FDG.Stages
{
    //TODO: There's lots of info that's specific to parts of the melee process.
    //Having a query handler like the combat metadata could be an improvement.
    public interface ICombatActionContext
    {
        public DataBinding<UnitData> AttackingUnit { get; }

        public DataBinding<UnitData> DefendingUnit { get; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons { get; }
        public IReadOnlyDictionary<IWeapon, int> AlreadyUsedWeapons { get; }

        public IModel InRangeAttackingModels { get; }

        public IModel InRangeDefendingModels { get; }

        public float AttackerRemainingWoundsAtStart { get; }

        public float DefenderRemainingWoundsAtStart { get; }

        public void AddResult<TResult>(TResult result);

        public bool QueryForResult<TResult>(out TResult result);

        public void BeginNewAttack(DataBinding<UnitData> defendingUnit);

        public void SetAttackWeapon(IWeapon weaponToConsume, out int weaponCount);

        public ICombatMetadata ConsumeAttackIntoContext(IGameContext gameContext);
    }

    public class CombatActionContext : ICombatActionContext
    {
        public DataBinding<UnitData> AttackingUnit { get; }

        public DataBinding<UnitData> DefendingUnit { get; private set; }

        public IModel InRangeAttackingModels { get; private set; }

        public IModel InRangeDefendingModels { get; private set; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons => _availableWeapons;

        public IReadOnlyDictionary<IWeapon, int> AlreadyUsedWeapons => _alreadyUsedWeapons;

        public float AttackerRemainingWoundsAtStart { get; private set; }

        public float DefenderRemainingWoundsAtStart { get; private set; }

        private ConcurrentDictionary<IWeapon, int> _availableWeapons;

        private ConcurrentDictionary<IWeapon, int> _alreadyUsedWeapons = new ConcurrentDictionary<IWeapon, int>();

        private QueryableResults _queryableResults = new QueryableResults();

        private PendingAttack _currentPendingAttack = null;

        public CombatActionContext(DataBinding<UnitData> attackingUnit)
        {
            AttackingUnit = attackingUnit;
            _availableWeapons = GetTypeSortedWeapons(attackingUnit.GetValue().GetMeleeWeapons());
            AttackerRemainingWoundsAtStart = attackingUnit.GetValue().RemainingWounds;
        }

        public void AddResult<TResult>(TResult result)
        {
            _queryableResults.AddResult(result);
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            return _queryableResults.QueryForResult(out result);
        }

        public void BeginNewAttack(DataBinding<UnitData> defendingUnit)
        {
            if(_currentPendingAttack != null)
            {
                //TODO: Allow for cancelling. 
                throw new InvalidOperationException($"Started attack before the last was consumed.");
            }

            DefendingUnit = defendingUnit;

            _currentPendingAttack = new PendingAttack();
            _currentPendingAttack.DefendingUnit = DefendingUnit;

            DefenderRemainingWoundsAtStart = DefendingUnit.GetValue().RemainingWounds;
        }

        public void SetAttackWeapon(IWeapon weaponToConsume, out int weaponCount)
        {
            if(_currentPendingAttack == null)
            {
                throw new InvalidOperationException($"Called {nameof(SetAttackWeapon)} before calling {nameof(BeginNewAttack)}.");
            }

            if (_availableWeapons.ContainsKey(weaponToConsume) == false)
            {
                throw new ArgumentException($"{nameof(CombatActionContext)}.{nameof(SetAttackWeapon)} called on weapon " +
                    $"that was not found in available list: {weaponToConsume.Name}");
            }

            _availableWeapons.TryRemove(weaponToConsume, out weaponCount);

            _alreadyUsedWeapons.TryAdd(weaponToConsume, weaponCount);

            _currentPendingAttack.WeaponType = weaponToConsume;
            _currentPendingAttack.WeaponCount = weaponCount;
        }

        public ICombatMetadata ConsumeAttackIntoContext(IGameContext gameContext)
        {
            if(_currentPendingAttack == null)
            {
                throw new InvalidOperationException($"Called {nameof(ConsumeAttackIntoContext)} when no attack was set.");
            }

            if(_currentPendingAttack.IsReady == false)
            {
                throw new InvalidOperationException($"Called {nameof(ConsumeAttackIntoContext)} when attack was not set up. " + 
                    "Must have all values set before consuming.");
            }

            CombatMetadata meleeCombatMetadata = new CombatMetadata(gameContext, AttackingUnit, 
                _currentPendingAttack.DefendingUnit, _currentPendingAttack.WeaponType, _currentPendingAttack.WeaponCount);

            _currentPendingAttack = null;

            return meleeCombatMetadata;
        }

        //TODO: Repeated in Ranged version. Move to static class.
        private ConcurrentDictionary<IWeapon, int> GetTypeSortedWeapons(List<IWeapon> weapons)
        {
            ConcurrentDictionary<IWeapon, int> weaponsAndCounts = new ConcurrentDictionary<IWeapon, int>();

            WeaponComparer comparer = new WeaponComparer();

            foreach (IWeapon newWeapon in weapons)
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

        private class PendingAttack //TODO: Duplicated from shooting.
        {
            public bool IsReady => DefendingUnit != default && WeaponType != default && WeaponCount != default;

            public DataBinding<UnitData> DefendingUnit = default;

            public IWeapon WeaponType = default;

            public int WeaponCount = default;
        }
    }


}