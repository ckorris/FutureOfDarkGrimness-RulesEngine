using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
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
                GameSystem = book.GameSystem,
                PointsLimit = list.PointsLimit,
                // Carry the book's rule/spell definitions so the engine's RuleValidator gate passes and unit
                // rule references resolve at load (audit finding 3).
                RuleDefinitions = new List<FDG.Rules.Definitions.SpecialRuleDefinition>(book.RuleDefinitions),
                Spells = new List<FDG.Rules.Definitions.SpellDefinition>(book.Spells),
                // #239: the faction's default effect sets ride the compiled army — from the book,
                // else the assigner's faction table (covers a book snapshot predating the fields).
                DefaultRangedEffectSet = book.DefaultRangedEffectSet ?? WeaponEffectAssigner.FactionDefaults(book.Faction).Ranged,
                DefaultMeleeEffectSet = book.DefaultMeleeEffectSet ?? WeaponEffectAssigner.FactionDefaults(book.Faction).Melee,
                Selections = list,
                Book = book,
            };

            foreach (BuilderUnit bu in list.Units)
                army.Units.Add(CompileUnitDetailed(book, bu).Unit);

            MergeCombinedUnits(list, army);

            CompileAuxiliaryUnits(book, army);

            return army;
        }

        // #197 P17: the rules whose TEXT argument names a unit spec the bearer places mid-game
        // ("Spawn(Spores [5])", "Split(Changelings [10])"). Kept as a plain set - only two rules do this
        // today; a third is a one-line addition here, and definition-declared metadata can replace the
        // set when the vocabulary genuinely grows.
        private static readonly HashSet<string> AuxiliarySpecRuleNames =
            new(StringComparer.OrdinalIgnoreCase) { "Spawn", "Split" };

        /// <summary>
        /// Compiles the auxiliary unit specs the army's Spawn/Split rules name into
        /// <see cref="ArmyListFile.AuxiliaryUnits"/>, keyed by the rule's exact text argument
        /// ("Spores [5]" -> the book's Spores unit compiled at 5 models, zero points, no upgrades).
        /// Recursive with a seen-guard: a spawned unit can itself carry Split (Change Horrors ->
        /// Lesser Change Horrors -> Changelings), and each link's target must ship too. A text naming
        /// no book unit is skipped - army load's own diagnostics warn when the rule then fires with
        /// nothing to place.
        /// </summary>
        private static void CompileAuxiliaryUnits(BookFile book, BuiltArmyFile army)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>();

            void EnqueueSpecsNamedBy(UnitFileEntry unit)
            {
                foreach (SpecialRuleEntry rule in unit.SpecialRules)
                {
                    if (rule is SpecialRuleEntry_Text text
                        && AuxiliarySpecRuleNames.Contains(text.Name)
                        && seen.Add(text.TextValue))
                    {
                        pending.Enqueue(text.TextValue);
                    }
                }
            }

            foreach (UnitFileEntry unit in army.Units) EnqueueSpecsNamedBy(unit);

            while (pending.Count > 0)
            {
                string specText = pending.Dequeue();
                (string targetName, int? count) = ParseAuxSpecText(specText);

                RosterUnit? roster = book.Units.FirstOrDefault(
                    u => string.Equals(u.Name, targetName, StringComparison.OrdinalIgnoreCase));
                if (roster == null) continue;

                UnitFileEntry aux = CompileUnitDetailed(book,
                    new BuilderUnit { RosterUnitId = roster.Id }).Unit;
                aux.Id = specText;        // the runtime lookup key IS the rule's argument text
                aux.ModelCount = count ?? aux.ModelCount;
                aux.PointCost = 0;        // placed by a rule, not bought
                aux.JoinsUnitId = null;

                (army.AuxiliaryUnits ??= new List<UnitFileEntry>()).Add(aux);
                EnqueueSpecsNamedBy(aux);
            }
        }

        // "Spores [5]" -> ("Spores", 5); a bare "Spores" -> ("Spores", null) = the roster's base size.
        private static (string Name, int? Count) ParseAuxSpecText(string specText)
        {
            string text = specText.Trim();
            int open = text.LastIndexOf('[');
            int close = text.EndsWith("]", StringComparison.Ordinal) ? text.Length - 1 : -1;
            if (open > 0 && close > open
                && int.TryParse(text.Substring(open + 1, close - open - 1), out int count))
            {
                return (text.Substring(0, open).Trim(), count);
            }

            return (text, null);
        }

        // #107 combined squads (decision 8, 2026-07-02): a BuilderUnit with CombinedWithId folds into its
        // partner copy so play sees one big unit — each copy having bought upgrades under its own normal
        // bounds. Only a link to another instance of the SAME roster unit merges; anything else (dangling
        // id, different roster unit) compiles as separate units and is the validator's business to flag.
        // The BuilderList itself is never mutated — it is the user's editable source of truth, embedded
        // as-authored in the saved army.
        private static void MergeCombinedUnits(BuilderList list, BuiltArmyFile army)
        {
            // army.Units aligns 1:1 with list.Units here, so resolve the pairs as object references first.
            var merges = new List<(UnitFileEntry Host, UnitFileEntry Absorbed)>();
            for (int i = 0; i < list.Units.Count; i++)
            {
                BuilderUnit bu = list.Units[i];
                if (string.IsNullOrEmpty(bu.CombinedWithId) || bu.CombinedWithId == bu.Id) continue;

                int hostIndex = list.Units.FindIndex(other =>
                    other != bu && other.Id == bu.CombinedWithId && other.RosterUnitId == bu.RosterUnitId);
                if (hostIndex < 0) continue;

                merges.Add((army.Units[hostIndex], army.Units[i]));
            }

            // Later list entries merge first, so an (illegal, validator-flagged) A←B←C chain still folds
            // C into B before B folds into A — nothing is lost even while the shape gets warned about.
            for (int m = merges.Count - 1; m >= 0; m--)
            {
                (UnitFileEntry host, UnitFileEntry absorbed) = merges[m];

                host.ModelCount += absorbed.ModelCount;
                host.PointCost += absorbed.PointCost;
                foreach (WeaponFileEntry weapon in absorbed.Weapons)
                    AddWeapon(host.Weapons, weapon, applications: 1);
                foreach (SpecialRuleEntry rule in absorbed.SpecialRules)
                    if (!host.SpecialRules.Contains(rule))
                        host.SpecialRules.Add(rule);

                army.Units.Remove(absorbed);

                // A hero joined to the absorbed copy follows it into the merged unit.
                if (!string.IsNullOrEmpty(absorbed.Id))
                    foreach (UnitFileEntry other in army.Units)
                        if (other.JoinsUnitId == absorbed.Id)
                            other.JoinsUnitId = host.Id;
            }
        }

        /// <summary>Per-row compiles in list order, WITHOUT #107 combined-pair merging — the row-aligned
        /// view the builder UI works from (two combined copies stay two rows). <see cref="Compile"/> is the
        /// play-time output, where combined pairs are one big unit. Throws on a malformed list, like
        /// <see cref="Compile"/>.</summary>
        public static List<UnitFileEntry> CompileRows(BookFile book, BuilderList list) =>
            list.Units.Select(bu => CompileUnitDetailed(book, bu).Unit).ToList();

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

            // #197 slice 0. Wargear whose rules are weapon-scoped AND whose section names a target weapon
            // ("Upgrade all Pulse Rifles with: Drone Controller (Reliable, Takedown)") attaches those rules
            // to that weapon, not to the unit — a Reliable rifle must not make its owner's melee taser hit
            // on 2+. Everything placed that way is recorded here so the rule-bundle fold below skips it.
            HashSet<string> weaponScopedNames = WeaponScopedRuleNames(book);
            HashSet<(string Item, SpecialRuleEntry Rule)> placedOnWeapons = new();
            List<(SpecialRuleEntry Rule, int Applications)> championMarks = new();

            // Apply in the book's section order, not click order — a later section may target weapons an
            // earlier one grants (e.g. "Replace one Shard Carbine" after "Replace all ... with Shard Carbines"),
            // so compilation must not depend on the sequence the user toggled things in.
            IEnumerable<UpgradeChoice> ordered = bu.Choices
                .OrderBy(c => Math.Max(0, roster.Sections.FindIndex(s => s.Id == c.SectionId)));

            // #323: Replace applications the loadout couldn't afford yet, retried after every section has
            // had its pass — book order alone can't satisfy a section whose targets a LATER one grants.
            List<(UpgradeSection Section, UpgradeOption Option, int Remaining)> starved = new();

            foreach (UpgradeChoice choice in ordered)
            {
                UpgradeSection section = roster.Sections.FirstOrDefault(s => s.Id == choice.SectionId)
                    ?? throw new InvalidOperationException($"Section '{choice.SectionId}' not found on unit '{roster.Id}'.");
                UpgradeOption option = section.Options.FirstOrDefault(o => o.Id == choice.OptionId)
                    ?? throw new InvalidOperationException($"Option '{choice.OptionId}' not found in section '{section.Id}'.");

                int applications = Applications(section, choice, unit, items);

                // #324: an "all" swap authored ABOVE a specialist swap on the same weapon used to eat the
                // whole pool before the specialist got a turn, silently deleting it (5 Pulse Rifles ->
                // "replace all with Pulse Carbines" + "replace one with a Plasma Rifle" produced five
                // carbines and no plasma, unpaid). "All" means "every one still on the model", so it yields
                // the copies its rivals have already been bought for and takes what remains.
                applications -= ReservedForLaterSwaps(roster, bu, choice, section);
                if (applications < 0) applications = 0;

                // #323: what the choice asked for minus what the loadout could afford. Parked, not lost —
                // a later section may still buy the missing target (the Titan's "Replace any Heavy Hammer"
                // is authored ABOVE the "Replace Titan Shield" that grants the second hammer).
                if (section.Variant == UpgradeVariant.Replace)
                {
                    int owed = OwedApplications(section, choice, applications);
                    if (owed > 0) starved.Add((section, option, owed));
                }

                if (applications <= 0) continue;

                // #218: a "Replace all X" price is flat for the whole unit - it is NOT multiplied by the
                // models it touches. Verified 2026-07-19 against the Havoc Brothers share list, where a
                // 10-pt Replace All on two 5-model units moved Army Forge's total by exactly 20, not 100.
                // Cost and effect part ways here: every eligible model still gets the swap (`applications`),
                // but the charge is levied once.
                unit.PointCost += option.Cost * (section.Affects == UpgradeAffects.All ? 1 : applications);

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
                        AddGains(unit, items, option, applications,
                            skipRules: CollectChampionMarks(section, option, applications, weaponScopedNames,
                                championMarks));
                        // Only non-Replace variants: a Replace has already removed its targets above, so
                        // there is no weapon left to attach to.
                        PlaceTargetedWeaponRules(unit, section, option, applications, weaponScopedNames, placedOnWeapons);
                        break;
                }
            }

            ApplyStarvedReplaces(unit, items, starved);

            // #197 Sergeant: champion marks attach AFTER every section has applied - a later "Replace all
            // Hand Weapons" must not eat the mark; the champion attacks with whatever the unit ends up
            // holding. One copy of each distinct weapon profile per application is the aggregate format's
            // expression of "this model's attacks": round-robin hands the marked copies to a model at
            // load, and the batcher already rolls a rule-bearing copy as its own volley (#027).
            //
            // The marked copy is then RENAMED ("Hand Weapon (Sergeant)"), for ATTRIBUTION: the row/log
            // lines name which copy did what ("Chose weapon: Rifle (Sergeant)"), which is what the player
            // wants to see. It began life as a correctness workaround - both weapon choosers keyed their
            // pools by NAME and a same-name split faulted the shoot stage, found in this slice's play
            // probe - but #306 profile-keyed them, so nothing depends on the rename any more. Owner's
            // call (2026-07-31) is to keep it for the attribution alone.
            foreach ((SpecialRuleEntry rule, int applications) in championMarks)
            {
                foreach (string weaponName in unit.Weapons.Select(w => w.Name).Distinct().ToList())
                {
                    AttachRuleToWeapons(unit.Weapons, weaponName, rule, applications);
                }

                string suffix = $" ({rule.PrintableName})";
                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    if (weapon.SpecialRules.Contains(rule) && !weapon.Name.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        weapon.Name += suffix;
                    }
                }
            }

            // Items are rule-bundles at runtime: fold their rules into the unit (see AddGrantedRule - deduped
            // by value, except a rated rule the unit already has, which stacks). A rule already placed on its
            // target weapon is skipped — it lives there, not on the unit. A weapon rule from an UNtargeted
            // item ("Toxic Cysts (Bane in Melee)") does fold in, and army-load (GameBootstrap) spreads it
            // across every weapon the unit carries.
            foreach (ItemEntry item in items)
                foreach (SpecialRuleEntry rule in item.Rules)
                    if (!placedOnWeapons.Contains((item.Name, rule)))
                        AddGrantedRule(unit.SpecialRules, rule);

            // #239: bake effect-set keys. An explicit book key survives the clone; anything still
            // unset gets its keyword/override match, so cross-faction tech (plasma, fusion...) beats
            // the army default. No match stays null — the army default covers it at load.
            foreach (WeaponFileEntry weapon in unit.Weapons)
                weapon.EffectSet ??= WeaponEffectAssigner.Match(book.Faction, weapon);

            return (unit, items);
        }

        /// <summary>
        /// #324 — copies a single-target "replace all" must leave behind for swaps authored BELOW it that
        /// compete for the same weapon and have already been bought. Zero for anything else, so the ordering
        /// of every other section is untouched.
        /// <para>Only rivals authored LATER are counted: an earlier one has already taken its copies out of
        /// the pool by the time this section applies, and reserving again would double-count it.</para>
        /// <para>Scoped to SINGLE-target all-swaps by owner decision (2026-08-02). The 88 corpus pairs whose
        /// all-swap names two weapons ("Replace all Heavy Pistols and CCWs") raise a separate question the
        /// aggregate weapon model can't answer cleanly - one model trading a pistol away leaves the unit
        /// holding more weapon copies than it has models - and are deferred to their own item. The 33
        /// single-target pairs have no such ambiguity: what is left is simply what "all" now means.</para>
        /// </summary>
        private static int ReservedForLaterSwaps(RosterUnit roster, BuilderUnit bu, UpgradeChoice choice,
            UpgradeSection section)
        {
            if (section.Variant != UpgradeVariant.Replace || section.Affects != UpgradeAffects.All
                || section.Targets.Count != 1)
                return 0;

            int index = roster.Sections.FindIndex(s => s.Id == section.Id);
            string target = ParseTarget(section.Targets[0]).Name;
            int reserved = 0;

            foreach (UpgradeChoice rival in bu.Choices)
            {
                if (rival == choice || rival.SectionId == section.Id) continue;

                int rivalIndex = roster.Sections.FindIndex(s => s.Id == rival.SectionId);
                if (rivalIndex <= index) continue;

                UpgradeSection rivalSection = roster.Sections[rivalIndex];
                if (!CompetesForTarget(rivalSection, target)) continue;

                reserved += rivalSection.Affects switch
                {
                    UpgradeAffects.One => 1,
                    UpgradeAffects.Any => rivalSection.MaxApplications > 0
                        ? Math.Min(Math.Max(0, rival.Count), rivalSection.MaxApplications)
                        : Math.Max(0, rival.Count),
                    _ => 0,
                };
            }

            return reserved;
        }

        /// <summary>#324 — whether a counted (One/Any) Replace draws on the same weapon an all-swap targets,
        /// so the all-swap must leave it a copy. Shared with the Forge, which has to offer the specialist
        /// swap the pool the compiler will actually honour.</summary>
        public static bool CompetesForTarget(UpgradeSection countedSwap, string allSwapTarget) =>
            countedSwap.Variant == UpgradeVariant.Replace
            && countedSwap.Affects != UpgradeAffects.All
            && countedSwap.Targets.Any(t => TargetMatches(ParseTarget(t).Name, allSwapTarget));

        /// <summary>#324 — the single target a "replace all" competes over, or null when the section is not a
        /// single-target all-swap (and so never yields).</summary>
        public static string? SingleAllSwapTarget(UpgradeSection section) =>
            section.Variant == UpgradeVariant.Replace && section.Affects == UpgradeAffects.All
            && section.Targets.Count == 1
                ? ParseTarget(section.Targets[0]).Name
                : null;

        /// <summary>
        /// #323 — how many applications a Replace choice still OWES after its first pass. The section order
        /// a book is authored in is not a dependency order: the Titan Lords mini-titans put "Replace any Heavy
        /// Hammer" ABOVE the "Replace Titan Shield" whose option buys the second hammer, so at the hammer
        /// section's turn only one hammer exists and the second swap was clamped away silently (the Forge let
        /// you set the count to 2, and an OPR import that carried two swaps lost one). What the choice asked
        /// for is banked here and retried once every section has had its pass.
        /// <para>All is the exception: "every match" is by definition whatever the unit holds when the section
        /// applies, so it owes nothing once it has swapped at least one target. It parks only when it found
        /// NO target at all (<see cref="EveryMatch"/> — take the whole pool whenever one turns up), which no
        /// bundled book exercises today.</para>
        /// </summary>
        private static int OwedApplications(UpgradeSection section, UpgradeChoice choice, int applied)
        {
            int chosen = Math.Max(0, choice.Count);
            if (chosen <= 0) return 0;

            return section.Affects switch
            {
                UpgradeAffects.All => applied > 0 ? 0 : EveryMatch,
                UpgradeAffects.Any => (section.MaxApplications > 0
                    ? Math.Min(chosen, section.MaxApplications)
                    : chosen) - applied,
                _ => 1 - applied,
            };
        }

        /// <summary>Stands in for "as many as the unit turns out to have" in a parked All (see
        /// <see cref="OwedApplications"/>); the retry clamps it to the live pool.</summary>
        private const int EveryMatch = int.MaxValue;

        /// <summary>
        /// #323 — settles the Replace applications <see cref="OwedApplications"/> banked, to a fixpoint: each
        /// pass spends whatever the loadout can now afford, and the loop stops as soon as a whole pass changes
        /// nothing (targets that never arrive simply stay unbought, as before). Every application charges its
        /// points here, so the shortfall costs what it would have cost in the first pass.
        /// </summary>
        private static void ApplyStarvedReplaces(UnitFileEntry unit, List<ItemEntry> items,
            List<(UpgradeSection Section, UpgradeOption Option, int Remaining)> starved)
        {
            bool progressed = starved.Count > 0;
            while (progressed)
            {
                progressed = false;
                for (int i = 0; i < starved.Count; i++)
                {
                    (UpgradeSection section, UpgradeOption option, int remaining) = starved[i];
                    if (remaining <= 0) continue;

                    // All strips every match of EACH target (max), the counted forms consume one of each per
                    // application (min) — the same split Applications() makes.
                    bool isAll = section.Affects == UpgradeAffects.All;
                    int pool = isAll
                        ? (section.Targets.Count == 0 ? 0 : section.Targets.Max(t => MatchedCount(unit.Weapons, items, t)))
                        : AvailableApplications(unit.Weapons, items, section.Targets);

                    int applications = Math.Min(remaining, pool);
                    if (applications <= 0) continue;

                    unit.PointCost += option.Cost * (isAll ? 1 : applications);
                    foreach (string target in section.Targets)
                        RemoveTarget(unit.Weapons, items, target, applications);
                    AddGains(unit, items, option, applications);

                    // An All that has now swapped is spent; a counted one keeps whatever it still owes.
                    starved[i] = (section, option, isAll ? 0 : remaining - applications);
                    progressed = true;
                }
            }
        }

        /// <summary>Rule names this book resolves to a <see cref="ERuleScope.Weapon"/>-scoped definition.
        /// A book definition overrides a core one of the same name (that is the registration order at army
        /// load), so it also decides the scope here.</summary>
        private static HashSet<string> WeaponScopedRuleNames(BookFile book)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SpecialRuleDefinition definition in CoreRuleCatalog.All)
                if (definition.Scope == ERuleScope.Weapon)
                    names.Add(definition.Name);

            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
                if (definition.Scope == ERuleScope.Weapon) names.Add(definition.Name);
                else names.Remove(definition.Name);

            return names;
        }

        // Moves an option's weapon-scoped gains onto the weapons its section targets, recording each
        // (item, rule) pair placed so it is not ALSO folded onto the unit. A target that matches no weapon
        // the unit currently carries (e.g. a scope bought without the carbine it upgrades) places nothing,
        // and the rule falls back to the unit-level path — the list validator's business, not ours.
        private static void PlaceTargetedWeaponRules(UnitFileEntry unit, UpgradeSection section, UpgradeOption option,
            int applications, HashSet<string> weaponScopedNames, HashSet<(string, SpecialRuleEntry)> placedOnWeapons)
        {
            if (section.Targets.Count == 0) return;

            foreach (ItemEntry item in option.ItemsGained)
                foreach (SpecialRuleEntry rule in item.Rules)
                    if (weaponScopedNames.Contains(RuleLookupName(rule)))
                        foreach (string target in section.Targets)
                            if (AttachRuleToWeapons(unit.Weapons, target, rule, applications))
                                placedOnWeapons.Add((item.Name, rule));

            foreach (SpecialRuleEntry rule in option.RulesGained)
                if (weaponScopedNames.Contains(RuleLookupName(rule)))
                    foreach (string target in section.Targets)
                        AttachRuleToWeapons(unit.Weapons, target, rule, applications);
        }

        // Attaches `rule` to up to `applications` copies of `target`. When the entry has more copies than
        // the upgrade bought, it splits: the upgraded copies become their own WeaponFileEntry (SameProfile
        // already keys on SpecialRules, so they never re-merge with the un-upgraded ones). Returns whether
        // any copy took the rule.
        private static bool AttachRuleToWeapons(List<WeaponFileEntry> weapons, string target, SpecialRuleEntry rule,
            int applications)
        {
            // Same quantity-prefix handling as the Replace path (#261): "2x Rapid Shard Cannon" names the
            // cannon and attaches to two copies per application.
            (string targetName, int perApplication) = ParseTarget(target);
            int remaining = applications * perApplication;
            bool attached = false;
            var upgraded = new List<WeaponFileEntry>();

            for (int i = 0; i < weapons.Count && remaining > 0; i++)
            {
                WeaponFileEntry weapon = weapons[i];
                if (!TargetMatches(weapon.Name, targetName)) continue;

                if (weapon.SpecialRules.Contains(rule))
                {
                    // Already carries it (a re-applied option, or the profile shipped with it).
                    remaining -= weapon.Quantity;
                    attached = true;
                    continue;
                }

                int take = Math.Min(weapon.Quantity, remaining);
                if (take == weapon.Quantity)
                {
                    weapon.SpecialRules.Add(rule);
                }
                else
                {
                    weapon.Quantity -= take;
                    WeaponFileEntry clone = CloneWeapon(weapon);
                    clone.Quantity = take;
                    clone.SpecialRules.Add(rule);
                    upgraded.Add(clone);
                }

                remaining -= take;
                attached = true;
            }

            weapons.AddRange(upgraded);
            return attached;
        }

        // The name a rule entry resolves under — an alias looks up the rule it renames, so its scope is
        // that rule's scope. Mirrors ArmyListRuleResolution.DescribeRuleEntry, which army-load uses.
        private static string RuleLookupName(SpecialRuleEntry rule) =>
            ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName;

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

        private static void AddGains(UnitFileEntry unit, List<ItemEntry> items, UpgradeOption option,
            int applications, List<SpecialRuleEntry>? skipRules = null)
        {
            foreach (WeaponFileEntry w in option.WeaponsGained)
                AddWeapon(unit.Weapons, w, applications);
            // #367: a BARE gained rule dedupes rather than stacking, unlike an item-granted one. No corpus
            // option grants a rated rule the unit already has except through an item, and the Army Forge
            // share JSON (OprListImporter) cannot tell a bare gain from an echo of the base rule - so both
            // list paths agree here. Revisit together if a book ever ships the bare shape.
            foreach (SpecialRuleEntry r in option.RulesGained)
                if (!unit.SpecialRules.Contains(r) && (skipRules == null || !skipRules.Contains(r)))
                    unit.SpecialRules.Add(r);
            foreach (ItemEntry it in option.ItemsGained)
                AddItem(items, it, applications);
        }

        /// <summary>
        /// #197 Sergeant: a WEAPON-scoped rule gained from a targets-less per-model section ("Upgrade up to
        /// three models with one: Sergeant") is a champion upgrade - it scopes to the bought model's own
        /// attacks, so it must never reach <c>unit.SpecialRules</c>, where army-load would spread it across
        /// every weapon copy (the whole-pool over-grant the per-model wording forbids). Such rules are
        /// recorded on <paramref name="marks"/> for the post-section attach and returned so
        /// <see cref="AddGains"/> skips them. Affects-All sections keep the unit-wide fold: "every model
        /// gets it" IS the whole pool. Corpus census 2026-07-29: Sergeant's 12 sites are the only occupants
        /// of this shape.
        /// </summary>
        private static List<SpecialRuleEntry>? CollectChampionMarks(UpgradeSection section, UpgradeOption option,
            int applications, HashSet<string> weaponScopedNames,
            List<(SpecialRuleEntry Rule, int Applications)> marks)
        {
            if (section.Targets.Count > 0 || section.Affects == UpgradeAffects.All) return null;

            List<SpecialRuleEntry>? collected = null;
            foreach (SpecialRuleEntry rule in option.RulesGained)
            {
                if (!weaponScopedNames.Contains(RuleLookupName(rule))) continue;

                marks.Add((rule, applications));
                (collected ??= new List<SpecialRuleEntry>()).Add(rule);
            }

            return collected;
        }

        /// <summary>How many times a Replace with these targets can apply — the MIN across targets of matched
        /// copies (weapons + items), since one application consumes one of each. Shared with the builder UI so
        /// it can gray out a "replace X" the unit has no X for (0 when no target list).</summary>
        public static int AvailableApplications(
            IEnumerable<WeaponFileEntry> weapons, IEnumerable<ItemEntry> items, IReadOnlyCollection<string> targets) =>
            targets.Count == 0 ? 0 : targets.Min(t => MatchedCount(weapons, items, t));

        // How many APPLICATIONS the unit's loadout supports: a target carrying a quantity prefix consumes
        // that many copies each time it applies, so 2 Rapid Shard Cannons afford exactly one "Replace 2x
        // Rapid Shard Cannon" (#261).
        private static int MatchedCount(IEnumerable<WeaponFileEntry> weapons, IEnumerable<ItemEntry> items, string target)
        {
            (string name, int perApplication) = ParseTarget(target);
            int copies = weapons.Where(w => TargetMatches(w.Name, name)).Sum(w => w.Quantity)
                       + items.Where(i => TargetMatches(i.Name, name)).Sum(i => i.Quantity);
            return copies / perApplication;
        }

        /// <summary>
        /// Splits an OPR upgrade target into its name and how many copies one application consumes.
        /// OPR writes the count into the target text ("2x Rapid Shard Cannon") rather than as a field, and
        /// nothing stripped it, so the whole string was matched against weapon names and never hit (#261):
        /// the swap silently applied nothing, cost nothing, and the Forge greyed the section out as "none
        /// to replace" while the unit visibly carried the weapon. Absent prefix means one copy.
        /// </summary>
        internal static (string Name, int PerApplication) ParseTarget(string target)
        {
            string trimmed = target.Trim();
            int x = trimmed.IndexOf('x');
            if (x > 0 && int.TryParse(trimmed[..x], out int count) && count > 0 && x + 1 < trimmed.Length
                && trimmed[x + 1] == ' ')
            {
                return (trimmed[(x + 2)..].Trim(), count);
            }
            return (trimmed, 1);
        }

        /// <summary>
        /// OPR upgrade targets are pluralised labels ("Energy Swords") that must match a singular weapon/item
        /// name ("Energy Sword"). Case-insensitive, and either side may carry the plural.
        /// <para>#324: "-es" plurals count too. OPR's own data targets "Bashes" against a weapon named "Bash"
        /// (Dwarf Guilds Guardians) - verified 2026-08-02 against the live army-books API, where `targets` is
        /// a list of display STRINGS with no id-based alternative, so their client singularises the label the
        /// same way. Stripping only a single trailing "s" left "bashe", which matched nothing: the section
        /// stripped the Pistols half and silently left all five Bashes on the unit, free. Corpus-wide this
        /// rule changes exactly that one target (pinned by ForgeCrossSectionReplaceShippedDataTests).</para>
        /// </summary>
        public static bool TargetMatches(string name, string target)
        {
            string a = name.Trim().ToLowerInvariant();
            string b = target.Trim().ToLowerInvariant();
            return IsSingularOf(a, b) || IsSingularOf(b, a);
        }

        // Whether `plural` is `singular` itself, or `singular` + an English plural suffix. Allocation-free:
        // this runs inside the Forge's per-frame recompile.
        private static bool IsSingularOf(string plural, string singular) =>
            plural == singular
            || (plural.Length == singular.Length + 1 && plural.EndsWith("s", StringComparison.Ordinal)
                && plural.StartsWith(singular, StringComparison.Ordinal))
            || (plural.Length == singular.Length + 2 && plural.EndsWith("es", StringComparison.Ordinal)
                && plural.StartsWith(singular, StringComparison.Ordinal));

        // Removes the target for `count` applications, consuming matching weapons first, then items. A
        // quantity-prefixed target ("2x Rapid Shard Cannon") consumes that many copies per application, so
        // the swap strips both cannons rather than leaving one behind (#261).
        private static void RemoveTarget(List<WeaponFileEntry> weapons, List<ItemEntry> items, string rawTarget, int count)
        {
            (string target, int perApplication) = ParseTarget(rawTarget);
            int remaining = count * perApplication;
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

        // #367 — folds a rule granted by an ITEM (the unit's own gear, or an item an upgrade added) into
        // its rule list, with OPR's additive semantic for rated rules: a granted Transport(6)
        // on a unit that already has Transport(6) makes it Transport(12), which is why Army Forge prints
        // the upgrade line as "Transport(+6)". Corpus-wide this fires for Tough (mounts), Impact, Transport
        // and Caster; it is keyed on "numeric" rather than a name list so a future book's rated rule stacks
        // by default instead of being silently swallowed. Un-rated rules still dedupe - a second Fearless
        // is not two Fearlesses.
        //
        // Only for GRANTS. Absorbing another unit's rules (a #107 combined pair, a joined hero) keeps the
        // plain dedupe: the host does not get tougher because its passenger is Tough.
        //
        // Internal: OprListImporter (#241) folds a share-link unit's `loadout` items through the same rule.
        internal static void AddGrantedRule(List<SpecialRuleEntry> rules, SpecialRuleEntry granted)
        {
            if (granted is SpecialRuleEntry_CoreNumeric numeric)
            {
                int at = rules.FindIndex(r => r is SpecialRuleEntry_CoreNumeric existing
                    && string.Equals(existing.Name, numeric.Name, StringComparison.Ordinal));
                if (at >= 0)
                {
                    // Constructed, never `with`: PrintableName is a positional member of the BASE record,
                    // so a copy-expression would keep the old "Transport(6)" text - and record equality
                    // keys on it, so the stale entry would also compare unequal to its own value.
                    var existing = (SpecialRuleEntry_CoreNumeric)rules[at];
                    rules[at] = new SpecialRuleEntry_CoreNumeric(existing.Name,
                        existing.NumericValue + numeric.NumericValue);
                    return;
                }
            }

            if (!rules.Contains(granted)) rules.Add(granted);
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

        // Internal: OprListImporter (#241) reuses this to consolidate identical weapon profiles.
        internal static void AddWeapon(List<WeaponFileEntry> weapons, WeaponFileEntry template, int applications)
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
            EffectSet = w.EffectSet,
            SpecialRules = new List<SpecialRuleEntry>(w.SpecialRules),
        };

        private static BaseFileEntry CloneBase(BaseFileEntry b) => new()
        {
            Shape = b.Shape, DiameterInches = b.DiameterInches,
            WidthInches = b.WidthInches, HeightInches = b.HeightInches,
        };
    }
}
