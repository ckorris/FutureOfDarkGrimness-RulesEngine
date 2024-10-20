
using System.Collections.Concurrent;

namespace FDG.Stages
{

    public interface IMeleeContext : ICommonContextItems
    {
        public IOfferStrikeBackHandler OfferStrikeBackHandler { get; }

        public IChooseMeleeWeaponHandler ChooseMeleeWeaponHandler { get; }

        public IUnit AttackingUnit { get; }

        public IUnit DefendingUnit { get; }

        public IMeleeCombatMetadata MeleeCombatMetadata { get; }

        public void BeginNewAttack(IUnit attackingUnit, IUnit defendingUnit);

        public void ChooseWeapon(IWeapon weaponToConsume, out int weaponCount);

        public void ClearCurrentAttack();

        public void ResetMeleeCombatMetadata();
    }

    public class MeleeContext : IMeleeContext
    {
        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public IOfferStrikeBackHandler OfferStrikeBackHandler { get; private set; }

        public IChooseMeleeWeaponHandler ChooseMeleeWeaponHandler { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IUnit DefendingUnit { get; private set; }

        public IMeleeCombatMetadata MeleeCombatMetadata { get; private set; }

        private ConcurrentDictionary<IWeapon, int> _availableWeapons;

        public MeleeContext(IChooseMeleeWeaponHandler chooseMeleeWeaponHandler, IOfferStrikeBackHandler offerStrikeBackHandler,
            ITextOutput textOutput, IDiceRoller diceRoller)
        {
            ChooseMeleeWeaponHandler = chooseMeleeWeaponHandler;
            OfferStrikeBackHandler = offerStrikeBackHandler;
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }

        public void BeginNewAttack(IUnit attackingUnit, IUnit defendingUnit)
        {
            AttackingUnit = attackingUnit;
            _availableWeapons = GetTypeSortedWeapons(attackingUnit.GetMeleeWeapons());
            MeleeCombatMetadata = new MeleeCombatMetadata(attackingUnit, defendingUnit, DiceRoller, TextOutput);
        }

        public void ChooseWeapon(IWeapon weaponToConsume, out int weaponCount)
        {
            if (_availableWeapons.ContainsKey(weaponToConsume) == false)
            {
                throw new ArgumentException($"{nameof(RangedContext)}.{nameof(ChooseWeapon)} called on weapon " +
                    $"that was not found in available list: {weaponToConsume.Name}");
            }

            _availableWeapons.TryRemove(weaponToConsume, out weaponCount);

            MeleeCombatMetadata.ChooseWeapon(weaponToConsume, weaponCount);

        }

        public void ClearCurrentAttack()
        {
            AttackingUnit = null;
            _availableWeapons = null;
            MeleeCombatMetadata = default;
        }

        public void ResetMeleeCombatMetadata()
        {
            throw new NotImplementedException();
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
    }
}