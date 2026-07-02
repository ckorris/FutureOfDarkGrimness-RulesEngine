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
                army.Units.Add(CompileUnitDetailed(book, bu).Unit);

            return army;
        }

        /// <summary>Compiles one unit and also returns its final wargear items (post-replaces), which the
        /// builder UI needs for display and target-availability. The item's rules are already flattened into
        /// the returned unit's SpecialRules — the item list is presentation/targeting metadata.</summary>
        public static (UnitFileEntry Unit, List<ItemEntry> Items) CompileUnitDetailed(BookFile book, BuilderUnit bu)
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
            List<ItemEntry> items = roster.Items.Select(CloneItem).ToList();

            // Apply in the book's section order, not click order — a later section may target weapons an
            // earlier one grants (e.g. "Replace one Shard Carbine" after "Replace all ... with Shard Carbines"),
            // so compilation must not depend on the sequence the user toggled things in.
            IEnumerable<UpgradeChoice> ordered = bu.Choices
                .OrderBy(c => Math.Max(0, roster.Sections.FindIndex(s => s.Id == c.SectionId)));

            foreach (UpgradeChoice choice in ordered)
            {
                UpgradeSection section = roster.Sections.FirstOrDefault(s => s.Id == choice.SectionId)
                    ?? throw new InvalidOperationException($"Section '{choice.SectionId}' not found on unit '{roster.Id}'.");
                UpgradeOption option = section.Options.FirstOrDefault(o => o.Id == choice.OptionId)
                    ?? throw new InvalidOperationException($"Option '{choice.OptionId}' not found in section '{section.Id}'.");

                int applications = Applications(section, choice, unit, items);
                if (applications <= 0) continue;

                unit.PointCost += option.Cost * applications;

                switch (section.Variant)
                {
                    case UpgradeVariant.Replace:
                        foreach (string target in section.Targets)
                            RemoveTarget(unit.Weapons, items, target, applications);
                        AddGains(unit, items, option, applications);
                        break;

                    case UpgradeVariant.AddModels:
                        unit.ModelCount += option.ModelsGained * applications;
                        AddGains(unit, items, option, applications);
                        break;

                    case UpgradeVariant.Upgrade:
                    case UpgradeVariant.PickN:
                        AddGains(unit, items, option, applications);
                        break;
                }
            }

            // Items are rule-bundles at runtime: fold their rules into the unit (deduped by value).
            foreach (ItemEntry item in items)
                foreach (SpecialRuleEntry rule in item.Rules)
                    if (!unit.SpecialRules.Contains(rule))
                        unit.SpecialRules.Add(rule);

            return (unit, items);
        }

        // How many times an option applies — drives cost + gain scaling, and (for Replace) is clamped so you
        // can never replace more targets than the unit actually has.
        //   One  → up to 1
        //   Any  → up to choice.Count, capped by MaxApplications (0 = unbounded) — a stepper
        //   All  → every matched target (per-model), as an on/off toggle
        // A combined target ("Energy Sword AND Combat Shield") consumes one of EACH per application, so the
        // One/Any cap is the MIN across targets — you can't take the swap without every part present. All
        // instead strips every match of each target, so its gain count is the MAX across targets.
        private static int Applications(UpgradeSection section, UpgradeChoice choice, UnitFileEntry unit, List<ItemEntry> items)
        {
            int chosen = Math.Max(0, choice.Count);
            if (section.Variant == UpgradeVariant.AddModels)
                return chosen;

            bool isReplace = section.Variant == UpgradeVariant.Replace;
            int availableMin = AvailableApplications(unit.Weapons, items, section.Targets);
            int availableMax = section.Targets.Count == 0 ? 0
                : section.Targets.Max(t => MatchedCount(unit.Weapons, items, t));

            int desired = section.Affects switch
            {
                UpgradeAffects.One => chosen > 0 ? 1 : 0,
                UpgradeAffects.All => chosen > 0 ? (isReplace ? availableMax : unit.ModelCount) : 0,
                UpgradeAffects.Any => Math.Min(chosen,
                    section.MaxApplications > 0 ? section.MaxApplications : (isReplace ? int.MaxValue : unit.ModelCount)),
                _ => chosen > 0 ? 1 : 0,
            };

            if (!isReplace) return desired;
            return Math.Min(desired, section.Affects == UpgradeAffects.All ? availableMax : availableMin);
        }

        private static void AddGains(UnitFileEntry unit, List<ItemEntry> items, UpgradeOption option, int applications)
        {
            foreach (WeaponFileEntry w in option.WeaponsGained)
                AddWeapon(unit.Weapons, w, applications);
            foreach (SpecialRuleEntry r in option.RulesGained)
                if (!unit.SpecialRules.Contains(r))
                    unit.SpecialRules.Add(r);
            foreach (ItemEntry it in option.ItemsGained)
                AddItem(items, it, applications);
        }

        /// <summary>How many times a Replace with these targets can apply — the MIN across targets of matched
        /// copies (weapons + items), since one application consumes one of each. Shared with the builder UI so
        /// it can gray out a "replace X" the unit has no X for (0 when no target list).</summary>
        public static int AvailableApplications(
            IEnumerable<WeaponFileEntry> weapons, IEnumerable<ItemEntry> items, IReadOnlyCollection<string> targets) =>
            targets.Count == 0 ? 0 : targets.Min(t => MatchedCount(weapons, items, t));

        private static int MatchedCount(IEnumerable<WeaponFileEntry> weapons, IEnumerable<ItemEntry> items, string target) =>
            weapons.Where(w => TargetMatches(w.Name, target)).Sum(w => w.Quantity)
            + items.Where(i => TargetMatches(i.Name, target)).Sum(i => i.Quantity);

        // OPR upgrade targets are pluralised labels ("Energy Swords") that must match a singular weapon/item
        // name ("Energy Sword"). Normalise case + a single trailing 's' so they line up.
        public static bool TargetMatches(string name, string target) => Normalize(name) == Normalize(target);

        private static string Normalize(string s)
        {
            s = s.Trim().ToLowerInvariant();
            return s.EndsWith("s") ? s[..^1] : s;
        }

        // Removes up to `count` copies of the target, consuming matching weapons first, then items.
        private static void RemoveTarget(List<WeaponFileEntry> weapons, List<ItemEntry> items, string target, int count)
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

            for (int i = 0; i < items.Count && remaining > 0; i++)
            {
                if (!TargetMatches(items[i].Name, target)) continue;
                int take = Math.Min(items[i].Quantity, remaining);
                items[i].Quantity -= take;
                remaining -= take;
            }
            items.RemoveAll(i => i.Quantity <= 0);
        }

        private static void AddItem(List<ItemEntry> items, ItemEntry template, int applications)
        {
            int add = Math.Max(1, template.Quantity) * applications;
            ItemEntry? existing = items.FirstOrDefault(i =>
                i.Name == template.Name && i.Rules.SequenceEqual(template.Rules));
            if (existing != null) existing.Quantity += add;
            else { ItemEntry clone = CloneItem(template); clone.Quantity = add; items.Add(clone); }
        }

        private static ItemEntry CloneItem(ItemEntry i) => new()
        {
            Name = i.Name, Quantity = i.Quantity,
            Rules = new List<SpecialRuleEntry>(i.Rules),
        };

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
