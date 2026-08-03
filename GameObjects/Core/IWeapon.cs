using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>
        /// #239: the resolved effect-set key for presentation — the weapon entry's explicit key,
        /// else the army's default for this weapon's ranged/melee kind, resolved once at army load.
        /// Opaque to the engine (front-ends map it to visuals/sounds); null means the front-end's
        /// global default.
        /// </summary>
        public string? EffectKey { get; }
    }

    public class WeaponComparer : IEqualityComparer<IWeapon>
    {
        // #239: EffectKey is deliberately excluded — it is presentation data, and batch grouping
        // must not split on it (same-name weapons share a key in practice anyway).
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

    /// <summary>
    /// #306: a stable, orderable string identifying a weapon PROFILE — name plus stat line plus the
    /// multiset of rules it carries (each rule's canonical definition name and its arguments).
    /// <para>
    /// The invariant that makes this usable as a dictionary key: <b>two weapons share a key exactly
    /// when <see cref="WeaponComparer"/> calls them equal</b>. The weapon pool
    /// (<see cref="WeaponPool.GroupByProfile"/>) dedupes by that comparer, so one pool entry is one key
    /// and the choosers can key their maps here without a collision. Before this existed both choosers
    /// keyed by bare <c>Weapon.Name</c>, and a unit carrying two same-named weapons with different
    /// profiles — a partial upgrade buying Precise for one of three rifles — faulted the state machine
    /// on <c>Dictionary.Add</c> mid-activation.
    /// </para>
    /// <para>
    /// Ordinal sorting on this key is <i>name-primary</i>: the field separator is below every printable
    /// character, so ordering by key is byte-identical to the old <c>OrderBy(Weapon.Name)</c> whenever
    /// names are unique, and merely breaks the tie deterministically when they are not. That is what
    /// #209's same-seed replay needs — a total order that no hash code can perturb.
    /// </para>
    /// Rule ordering does not affect the key (the rule signatures are sorted), matching the comparer's
    /// order-insensitive rule comparison. The alias a rule was authored under does not affect it either:
    /// the canonical <see cref="SpecialRuleDefinition.Name"/> is used, because the comparer identifies
    /// rules by definition and treats two aliases of one rule as the same weapon.
    /// </summary>
    public static class WeaponProfileKey
    {
        /// <summary>Separates the key's top-level fields. Below every printable character, so ordinal
        /// sorting on the key orders by name first. Public so tests can split a key apart.</summary>
        public const char FieldSeparator = '\u0001';

        private const char RuleSeparator = '\u0002';
        private const char ArgumentSeparator = '\u0003';

        public static string For(IWeapon weapon)
        {
            List<string> ruleSignatures = new List<string>(weapon.RuleDefinitions.Count);
            foreach (ResolvedRule rule in weapon.RuleDefinitions)
            {
                StringBuilder signature = new StringBuilder(rule.Definition.Name);
                foreach (RuleArgument argument in rule.Arguments)
                {
                    signature.Append(ArgumentSeparator).Append(argument switch
                    {
                        RuleArgument.Int intArgument =>
                            intArgument.Value.ToString(CultureInfo.InvariantCulture),
                        RuleArgument.Str stringArgument => stringArgument.Value,
                        // A kind added later still keys deterministically off its record ToString rather
                        // than silently collapsing two attachments into one profile.
                        _ => argument.ToString(),
                    });
                }
                ruleSignatures.Add(signature.ToString());
            }

            // Sorted, so the key is a multiset — the comparer's HaveSameRules is order-insensitive.
            ruleSignatures.Sort(StringComparer.Ordinal);

            // "R" round-trips the float exactly, so two ranges compare equal here precisely when they
            // compare equal with == in the comparer (ranges are finite and non-negative in practice).
            return string.Join(FieldSeparator,
                weapon.Name,
                weapon.RangeInches.ToString("R", CultureInfo.InvariantCulture),
                weapon.Attacks.ToString(CultureInfo.InvariantCulture),
                weapon.ArmorPenetration.ToString(CultureInfo.InvariantCulture),
                string.Join(RuleSeparator, ruleSignatures));
        }
    }

    /// <summary>
    /// Builds the (weapon -> how many copies the unit carries) pool that every combat stage works from.
    /// </summary>
    public static class WeaponPool
    {
        /// <summary>
        /// Groups weapons by PROFILE: one entry per distinct <see cref="WeaponProfileKey"/> — equivalently,
        /// per <see cref="WeaponComparer"/> equivalence class — keyed by the first instance seen of that
        /// profile and valued by how many copies were passed in. The weapon lists that feed this hold one
        /// instance per carrying model, so duplicates are the normal case, and a unit carrying two
        /// same-named weapons with different rules yields two entries rather than one merged (or a fault).
        /// </summary>
        public static ConcurrentDictionary<Weapon, int> GroupByProfile(IEnumerable<Weapon> weapons)
        {
            ConcurrentDictionary<Weapon, int> weaponsAndCounts = new ConcurrentDictionary<Weapon, int>();
            Dictionary<string, Weapon> firstOfProfile = new Dictionary<string, Weapon>(StringComparer.Ordinal);

            foreach (Weapon weapon in weapons)
            {
                string key = WeaponProfileKey.For(weapon);
                if (firstOfProfile.TryGetValue(key, out Weapon? representative))
                {
                    weaponsAndCounts[representative]++;
                }
                else
                {
                    firstOfProfile[key] = weapon;
                    weaponsAndCounts[weapon] = 1;
                }
            }

            return weaponsAndCounts;
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

        /// <inheritdoc cref="IWeapon.EffectKey"/>
        public string? EffectKey { get; }

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

        public Weapon(string name, float rangeInches, int attacks, int armorPenetration,
            string? effectKey = null)
        {
            Name = name;
            RangeInches = rangeInches;
            Attacks = attacks;
            ArmorPenetration = armorPenetration;
            EffectKey = effectKey;
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

        // #325: RuleDefinitions is [JsonIgnore], so a weapon that crossed ANY Newtonsoft hop - a stage
        // request reaching a remote player, the synced store, a save - used to arrive with an empty live
        // list and only the blob, and every reader of RuleDefinitions on the receiving side (weapon stat
        // lines, rule tooltips, the AI) silently saw a rule-less weapon. Restore the invariant at the
        // boundary instead of asking each consumer to remember to rehydrate (the reply path's explicit
        // RehydrateRules in ChooseRangedAttackStage.ResolveChosenWeapon and StoreReplay's sweep remain as
        // harmless no-ops). The blob is self-contained (#095), so no resolver or registry is needed here.
        [System.Runtime.Serialization.OnDeserialized]
        private void OnDeserialized(System.Runtime.Serialization.StreamingContext context)
            => RehydrateRules();
    }
}
