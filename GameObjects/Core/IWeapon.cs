using FDG.Rules.Dispatch;
using Newtonsoft.Json;
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

        /// <summary>
        /// The #042 special-rule definitions this weapon carries (#027). Resolved from the
        /// army file's per-weapon rule names at load; applies only to attacks made with
        /// this weapon. Mirrors <see cref="IUnit.RuleDefinitions"/>.
        /// </summary>
        public IReadOnlyList<ResolvedRule> RuleDefinitions { get; }
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

        private readonly List<ResolvedRule> _ruleDefinitions = new();

        /// <summary>
        /// Like <see cref="UnitData.RuleDefinitions"/>, deliberately not serialized: rule
        /// names in the army file are the persisted form, re-resolved against the host's
        /// registry at load.
        /// </summary>
        [JsonIgnore] public IReadOnlyList<ResolvedRule> RuleDefinitions => _ruleDefinitions;

        public Weapon(string name, float rangeInches, int attacks, int armorPenetration, HashSet<ISpecialRule_Weapon> specialRules)
        {
            Name = name;
            RangeInches = rangeInches;
            Attacks = attacks;
            ArmorPenetration = armorPenetration;
            SpecialRules = specialRules;
        }

        /// <summary>
        /// Attaches a resolved special-rule definition to this weapon. Post-construction
        /// (army-load / harness), mirroring <see cref="UnitData.AttachRuleDefinition"/>.
        /// </summary>
        public void AttachRuleDefinition(ResolvedRule rule) => _ruleDefinitions.Add(rule);
    }
}