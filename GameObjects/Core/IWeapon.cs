using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FDG
{
    public interface IWeapon
    {
        public string Name { get; }

        public float RangeInches { get; }

        public int Attacks { get; }

        public int ArmorPenetration { get; }

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
            return x.Name == y.Name && x.RangeInches == y.RangeInches
                && x.Attacks == y.Attacks && x.ArmorPenetration == y.ArmorPenetration
                && HaveSameRules(x, y);
        }

        public int GetHashCode(IWeapon obj)
        {
            // Rules are deliberately left out of the hash (only the cheap stats) — weapons
            // differing only in rules land in one bucket and fall through to Equals.
            return System.HashCode.Combine(obj.Name, obj.RangeInches, obj.Attacks, obj.ArmorPenetration);
        }

        /// <summary>
        /// Order-insensitive comparison of the two weapons' rule attachments by definition
        /// identity + argument values, counting multiplicity — so a Takedown rifle never
        /// groups with a plain rifle of the same stat line (#027).
        /// </summary>
        private static bool HaveSameRules(IWeapon x, IWeapon y)
        {
            if (x.RuleDefinitions.Count != y.RuleDefinitions.Count)
            {
                return false;
            }

            List<ResolvedRule> unmatched = y.RuleDefinitions.ToList();
            foreach (ResolvedRule rule in x.RuleDefinitions)
            {
                int match = unmatched.FindIndex(r => r.Definition == rule.Definition
                    && r.Arguments.SequenceEqual(rule.Arguments));
                if (match < 0)
                {
                    return false;
                }
                unmatched.RemoveAt(match);
            }

            return true;
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

            foreach (ResolvedRule rule in weapon.RuleDefinitions)
            {
                sb.Append($", {rule.RequestedName}");
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

        private readonly List<ResolvedRule> _ruleDefinitions = new();

        /// <summary>
        /// Like <see cref="UnitData.RuleDefinitions"/>, deliberately not serialized: rule
        /// names in the army file are the persisted form, re-resolved against the host's
        /// registry at load.
        /// </summary>
        [JsonIgnore] public IReadOnlyList<ResolvedRule> RuleDefinitions => _ruleDefinitions;

        // #095: persisted form of RuleDefinitions (which is [JsonIgnore]), so weapon rules survive a
        // save/load resume even though army files are vestigial there. See RuleAttachmentPersistence.
        [JsonProperty] private string? _ruleDefinitionsJson;

        public Weapon(string name, float rangeInches, int attacks, int armorPenetration)
        {
            Name = name;
            RangeInches = rangeInches;
            Attacks = attacks;
            ArmorPenetration = armorPenetration;
        }

        /// <summary>
        /// Attaches a resolved special-rule definition to this weapon. Post-construction
        /// (army-load / harness), mirroring <see cref="UnitData.AttachRuleDefinition"/>.
        /// </summary>
        public void AttachRuleDefinition(ResolvedRule rule)
        {
            _ruleDefinitions.Add(rule);
            _ruleDefinitionsJson = RuleAttachmentPersistence.Serialize(_ruleDefinitions);
        }

        /// <summary>
        /// #095: replays the persisted rule blob back onto <see cref="RuleDefinitions"/> after a load,
        /// mirroring <see cref="UnitData.RehydrateRules"/>. Idempotent; no-op unless the live list is empty.
        /// </summary>
        public void RehydrateRules()
        {
            if (_ruleDefinitions.Count == 0 && !string.IsNullOrEmpty(_ruleDefinitionsJson))
            {
                _ruleDefinitions.AddRange(RuleAttachmentPersistence.Deserialize(_ruleDefinitionsJson));
            }
        }
    }
}
