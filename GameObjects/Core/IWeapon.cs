using System.Collections.Generic;
using System.Text;

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

        /// <summary>
        /// Returns human-readable text about a weapon that would read like it would on a data sheet.
        /// </summary>
        public static string GetWeaponNameAndStats(this IWeapon weapon)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{weapon.Name} - ");
            if (weapon.IsRanged())
            {
                sb.Append(weapon.RangeInches + "\", ");
            }
            sb.Append($"A{weapon.Attacks}, AP{weapon.ArmorPenetration}");

            foreach (ISpecialRule_Weapon specialRule in weapon.SpecialRules) //TODO: Use actual name.
            {
                sb.Append($", {specialRule.GetType().Name}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns human-readable text about a weapon that would read like it would on a data sheet, including
        /// how many weapons there are, which you supply as a parameter.
        /// </summary>
        public static string GetWeaponNameAndStats(this IWeapon weapon, int weaponCount)
        {
            return $"{weaponCount}x {weapon.GetWeaponNameAndStats()}";
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