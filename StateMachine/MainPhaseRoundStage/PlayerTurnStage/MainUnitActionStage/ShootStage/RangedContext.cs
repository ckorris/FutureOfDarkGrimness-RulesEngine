
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace FDG.StateMachine
{

    public interface IRangedContext : ICommonContextItems
    {
        public IChooseRangedTargetHandler ChooseRangedTargetHandler { get; }

        public IChooseRangedWeaponHandler ChooseRangedWeaponHandler { get; }

        public IUnit AttackingUnit { get; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons { get; }

        public IReadOnlyList<IUnit> AvailableTargetUnits { get; }

        public IRangedCombatMetadata CombatMetaData { get; }

        public void BeginNewAttack(IUnit attackingUnit, List<IUnit> availableTargetUnits);

        public void ChooseWeapon(IWeapon weaponToConsume, out int weaponCount);

        public void ChooseTargetUnit(IUnit targetUnit);

        public void ClearCurrentAttack();
    }

    public class RangedContext : IRangedContext
    {
        public ITextOutput TextOutput { get; private set; }

        public IChooseRangedWeaponHandler ChooseRangedWeaponHandler { get; private set; }

        public IChooseRangedTargetHandler ChooseRangedTargetHandler { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons => _availableWeapons;

        public IReadOnlyList<IUnit> AvailableTargetUnits { get; private set; }

        public IRangedCombatMetadata CombatMetaData { get; private set; }


        private ConcurrentDictionary<IWeapon, int> _availableWeapons;


        public RangedContext(IChooseRangedWeaponHandler chooseRangedWeaponHandler,
            IChooseRangedTargetHandler chooseRangedTargetHandler, ITextOutput textOutput, IDiceRoller diceRoller)
        {
            ChooseRangedWeaponHandler = chooseRangedWeaponHandler;
            ChooseRangedTargetHandler = chooseRangedTargetHandler;
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }

        public void BeginNewAttack(IUnit attackingUnit, List<IUnit> availableTargetUnits)
        {
            AttackingUnit = attackingUnit;
            AvailableTargetUnits = availableTargetUnits;
            _availableWeapons = GetTypeSortedWeapons(attackingUnit.GetRangedWeapons());
            CombatMetaData = new RangedCombatMetadata(attackingUnit, DiceRoller, TextOutput);
        }

        public void ChooseWeapon(IWeapon weaponToConsume, out int weaponCount)
        {
            if(_availableWeapons.ContainsKey(weaponToConsume) == false)
            {
                throw new System.ArgumentException($"{nameof(RangedContext)}.{nameof(ChooseWeapon)} called on weapon " + 
                    $"that was not found in available list: {weaponToConsume.Name}");
            }

            _availableWeapons.TryRemove(weaponToConsume, out weaponCount);

            CombatMetaData.ChooseWeapon(weaponToConsume, weaponCount);
        }

        public void ChooseTargetUnit(IUnit targetUnit)
        {
            if(AvailableTargetUnits.Contains(targetUnit) == false)
            {
                throw new System.ArgumentException($"{nameof(RangedContext)}.{nameof(ChooseTargetUnit)} called on unit " +
                    $"that was not found in available list: {targetUnit.Name}");
            }

            CombatMetaData.ChooseTarget(targetUnit);
        }

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

        public void ClearCurrentAttack()
        {
            AttackingUnit = null;
            AvailableTargetUnits = null;
            _availableWeapons = null;
            CombatMetaData = null;
        }
    }
}
