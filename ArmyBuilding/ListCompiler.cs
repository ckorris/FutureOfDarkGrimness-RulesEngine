using System;
using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #153 (P0) — the heart of the feature: turn a BookFile + a BuilderList of selections into the playable
    // ArmyListFile the engine already consumes, wrapped as a BuiltArmyFile that also embeds the selections +
    // a full book snapshot (so one .fdgarmy both plays and re-opens for editing).
    //
    // Model of the world (audit finding 1): a UnitFileEntry carries unit-level weapons with per-type Quantity;
    // the engine expands + round-robins them across models. So all upgrades are AGGREGATE operations on weapon
    // counts — "replace one Rifle with a Heavy" = Rifle.Quantity--, Heavy.Quantity++. Applying an option N
    // times (see Applications) multiplies its cost and its gains by N.
    //
    // Totality: Compile throws only on a malformed list (unknown roster/section/option id) — a programming
    // error, not a legal user selection. Any legal BuilderList compiles without throwing.
    public static class ListCompiler
    {
        public static BuiltArmyFile Compile(BookFile book, BuilderList list)
        {
            if (book is null) throw new ArgumentNullException(nameof(book));
            if (list is null) throw new ArgumentNullException(nameof(list));

            var army = new BuiltArmyFile
            {
                Name = string.IsNullOrWhiteSpace(list.Name) ? book.Name : list.Name,
                Faction = book.Faction,
                PointsLimit = list.PointsLimit,
                // Carry the book's rule/spell definitions so the engine's RuleValidator gate passes and unit
                // rule references resolve at load (audit finding 3).
                RuleDefinitions = new List<FDG.Rules.Definitions.SpecialRuleDefinition>(book.RuleDefinitions),
                Spells = new List<FDG.Rules.Definitions.SpellDefinition>(book.Spells),
                Selections = list,
                Book = book,
            };

            foreach (BuilderUnit bu in list.Units)
                army.Units.Add(CompileUnit(book, bu));

            return army;
        }

        private static UnitFileEntry CompileUnit(BookFile book, BuilderUnit bu)
        {
            RosterUnit roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId)
                ?? throw new InvalidOperationException($"Roster unit '{bu.RosterUnitId}' not found in book '{book.Name}'.");

            var unit = new UnitFileEntry
            {
                Name = roster.Name,
                Quality = roster.Quality,
                Defense = roster.Defense,
                ModelCount = roster.BaseModelCount,
                PointCost = roster.BasePointCost,
                Base = CloneBase(roster.Base),
                Id = bu.Id,
                JoinsUnitId = bu.JoinsUnitId,
                SpecialRules = new List<SpecialRuleEntry>(roster.Rules),
                Weapons = roster.Weapons.Select(CloneWeapon).ToList(),
            };

            foreach (UpgradeChoice choice in bu.Choices)
            {
                UpgradeSection section = roster.Sections.FirstOrDefault(s => s.Id == choice.SectionId)
                    ?? throw new InvalidOperationException($"Section '{choice.SectionId}' not found on unit '{roster.Id}'.");
                UpgradeOption option = section.Options.FirstOrDefault(o => o.Id == choice.OptionId)
                    ?? throw new InvalidOperationException($"Option '{choice.OptionId}' not found in section '{section.Id}'.");

                int applications = Applications(section, choice, unit);
                if (applications <= 0) continue;

                unit.PointCost += option.Cost * applications;

                switch (section.Variant)
                {
                    case UpgradeVariant.Replace:
                        foreach (string target in section.Targets)
                            RemoveWeapon(unit.Weapons, target, applications);
                        AddGains(unit, option, applications);
                        break;

                    case UpgradeVariant.AddModels:
                        unit.ModelCount += option.ModelsGained * applications;
                        AddGains(unit, option, applications);
                        break;

                    case UpgradeVariant.Upgrade:
                    case UpgradeVariant.PickN:
                        AddGains(unit, option, applications);
                        break;
                }
            }

            return unit;
        }

        // How many times an option applies — drives cost + gain scaling, and (for Replace) is clamped so you
        // can never replace more targets than the unit actually has.
        //   One  → up to 1
        //   Any  → up to choice.Count, capped by MaxApplications (0 = unbounded) — a stepper
        //   All  → every matched target (per-model), as an on/off toggle
        private static int Applications(UpgradeSection section, UpgradeChoice choice, UnitFileEntry unit)
        {
            int chosen = Math.Max(0, choice.Count);
            if (section.Variant == UpgradeVariant.AddModels)
                return chosen;

            bool isReplace = section.Variant == UpgradeVariant.Replace;
            int availableSum = AvailableTargets(unit.Weapons, section.Targets);
            int availableMax = section.Targets.Count == 0 ? 0 : section.Targets.Max(t => MatchedCount(unit.Weapons, t));

            int desired = section.Affects switch
            {
                UpgradeAffects.One => chosen > 0 ? 1 : 0,
                UpgradeAffects.All => chosen > 0 ? (isReplace ? availableMax : unit.ModelCount) : 0,
                UpgradeAffects.Any => Math.Min(chosen,
                    section.MaxApplications > 0 ? section.MaxApplications : (isReplace ? int.MaxValue : unit.ModelCount)),
                _ => chosen > 0 ? 1 : 0,
            };

            if (!isReplace) return desired;
            return Math.Min(desired, section.Affects == UpgradeAffects.All ? availableMax : availableSum);
        }

        private static void AddGains(UnitFileEntry unit, UpgradeOption option, int applications)
        {
            foreach (WeaponFileEntry w in option.WeaponsGained)
                AddWeapon(unit.Weapons, w, applications);
            foreach (SpecialRuleEntry r in option.RulesGained)
                if (!unit.SpecialRules.Contains(r))
                    unit.SpecialRules.Add(r);
        }

        /// <summary>Total copies of weapons matching any of <paramref name="targets"/>. Shared with the builder
        /// UI so it can gray out a "replace X" the unit has no X for.</summary>
        public static int AvailableTargets(IEnumerable<WeaponFileEntry> weapons, IEnumerable<string> targets) =>
            targets.Sum(t => MatchedCount(weapons, t));

        private static int MatchedCount(IEnumerable<WeaponFileEntry> weapons, string target) =>
            weapons.Where(w => TargetMatches(w.Name, target)).Sum(w => w.Quantity);

        // OPR upgrade targets are pluralised labels ("Energy Swords") that must match a singular weapon name
        // ("Energy Sword"). Normalise case + a single trailing 's' so they line up.
        public static bool TargetMatches(string weaponName, string target) => Normalize(weaponName) == Normalize(target);

        private static string Normalize(string s)
        {
            s = s.Trim().ToLowerInvariant();
            return s.EndsWith("s") ? s[..^1] : s;
        }

        private static void RemoveWeapon(List<WeaponFileEntry> weapons, string target, int count)
        {
            int remaining = count;
            for (int i = 0; i < weapons.Count && remaining > 0; i++)
            {
                if (!TargetMatches(weapons[i].Name, target)) continue;
                int take = Math.Min(weapons[i].Quantity, remaining);
                weapons[i].Quantity -= take;
                remaining -= take;
            }
            weapons.RemoveAll(w => w.Quantity <= 0);
        }

        private static void AddWeapon(List<WeaponFileEntry> weapons, WeaponFileEntry template, int applications)
        {
            int add = Math.Max(1, template.Quantity) * applications;
            WeaponFileEntry? existing = weapons.FirstOrDefault(w => SameProfile(w, template));
            if (existing != null) existing.Quantity += add;
            else { WeaponFileEntry clone = CloneWeapon(template); clone.Quantity = add; weapons.Add(clone); }
        }

        // Two weapons merge only if their whole profile matches — name alone isn't enough (a rule difference
        // would otherwise be silently lost into an existing entry).
        private static bool SameProfile(WeaponFileEntry a, WeaponFileEntry b) =>
            a.Name == b.Name && a.RangeInches == b.RangeInches && a.Attacks == b.Attacks &&
            a.ArmorPenetration == b.ArmorPenetration && a.SpecialRules.SequenceEqual(b.SpecialRules);

        private static WeaponFileEntry CloneWeapon(WeaponFileEntry w) => new()
        {
            Name = w.Name, Quantity = w.Quantity, RangeInches = w.RangeInches,
            Attacks = w.Attacks, ArmorPenetration = w.ArmorPenetration,
            SpecialRules = new List<SpecialRuleEntry>(w.SpecialRules),
        };

        private static BaseFileEntry CloneBase(BaseFileEntry b) => new()
        {
            Shape = b.Shape, DiameterInches = b.DiameterInches,
            WidthInches = b.WidthInches, HeightInches = b.HeightInches,
        };
    }
}
