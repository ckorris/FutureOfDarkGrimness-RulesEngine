using System.Collections.Generic;

namespace FDG
{
    public interface IWeapon
    {
        public string Name { get; }

        public float RangeInches { get; }

        public int Attacks { get; }

        public int ArmorPenetration { get; }

        public HashSet<ISpecialRule_Weapon> SpecialRules { get; }
    }

    public class WeaponComparer : IEqualityComparer<IWeapon>
    {
        public bool Equals(IWeapon x, IWeapon y)
        {
            //TODO: Handle special rules. 

            return x.Name == y.Name && x.RangeInches == y.RangeInches
                && x.Attacks == y.Attacks && x.ArmorPenetration == y.ArmorPenetration;
        }

        public int GetHashCode(IWeapon obj)
        {
            throw new System.NotImplementedException();
        }
    }

    public static class IWeaponExtensions
    {
        public static bool IsRanged(this IWeapon weapon)
        {
            return weapon.RangeInches > 0f;
        }

        public static bool IsMelee(this IWeapon weapon)
        {
            return !IsRanged(weapon);
        }
    }

    public class Weapon : IWeapon
    {
        public string Name { get; }

        public float RangeInches { get; }

        public int Attacks { get; }

        public int ArmorPenetration { get; }

        public HashSet<ISpecialRule_Weapon> SpecialRules { get; }

        public Weapon(string name, float rangeInches, int attacks, int armorPenetration, HashSet<ISpecialRule_Weapon> specialRules)
        {
            Name = name;
            RangeInches = rangeInches;
            Attacks = attacks;
            ArmorPenetration = armorPenetration;
            SpecialRules = specialRules;
        }
    }
}