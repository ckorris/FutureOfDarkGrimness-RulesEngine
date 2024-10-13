using System.Collections;
using System.Collections.Generic;

namespace FDG
{
    public class WeaponAttackSet
    {
        public IWeapon Weapon { get; }

        public int SuccessRollNeeded { get; set; }

        public WeaponAttackSet(IWeapon weapon, int successRollNeeded)
        {
            Weapon = weapon;
            SuccessRollNeeded = successRollNeeded;
        }

        public static List<WeaponAttackSet> GetAttackSetsForWeapons(IEnumerable<IWeapon> weapons, int successRollNeeded)
        {
            List<WeaponAttackSet> weaponAttackSets = new List<WeaponAttackSet>();

            foreach (IWeapon weapon in weapons)
            {
                weaponAttackSets.Add(new WeaponAttackSet(weapon, successRollNeeded));
            }

            return weaponAttackSets;
        }
    }
}