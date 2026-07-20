using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #241 — OnePageRules Army Forge SHARE-LIST importer: the resolved list JSON a share link serves
    // (/api/tts?id=<shareId>) → a plain, playable ArmyListFile. The catalog counterpart is OprBookImporter
    // (/api/army-books/<uid> → BookFile); this consumes the OTHER shape — per-unit final stats and `loadout`
    // as Army Forge itself computed them — so gear is OPR's verbatim and never re-derived through
    // ListCompiler (which keeps #218's Replace-All cost bug out of imports by construction).
    //
    // POINTS, precisely (corrected 2026-07-19 — v1/v2 assumed per-unit costs were resolved, and imported
    // lists came in LIGHT by every upgrade point): a share list's per-unit `cost` is the unit's BASE cost.
    // The list's true total lives only in the top-level `listPoints`. The gap between them is upgrade
    // points, and it is NOT recoverable per unit: OPR omits the `cost` key entirely on options it prices in
    // its internal algorithm (see UpgradeOption.CostUnpriced), on both the list and book endpoints. So the
    // import trusts `listPoints` for the total and parks the unattributable remainder in
    // ArmyListFile.UnattributedPoints rather than inventing a per-unit split.
    //
    // The list JSON carries NO version field, so the version gate keys off each referenced army book's
    // CURRENT versionString (the caller fetches /api/army-books/{armyId} and passes the JSON in): a book
    // outside the supported major.minor throws OprVersionMismatchException. OPR never supports old versions
    // once they bump — refusing loudly is the designed behavior (design sign-off 2026-07-16).
    //
    // Recorded fidelity gaps (v1 — warned, not silently dropped):
    //   • Campaign/narrative features (campaignMode, narrativeMode, per-unit xp/traits) are not imported (#242).
    //   • attacksMultiplier != 1 on a loadout weapon is unverified corpus territory — attacks stay as-is.
    //   • selectedUpgrades is parsed defensively for RULE gains only (dedupe-merged); `loadout` is trusted
    //     as the sole source of weapons/items.
    public static partial class OprListImporter
    {
        /// <summary>The OPR army-book major.minor this game's data and rule implementations target. A book
        /// whose versionString is outside this prefix refuses to import (see OprVersionMismatchException).
        /// Bump when the bundled Assets/Books snapshots are re-imported against a newer OPR release.</summary>
        public const string SupportedVersionPrefix = "3.5";

        /// <summary>The only OPR game system this game implements (Grimdark Future).</summary>
        public const string SupportedGameSystem = "gf";

        /// <summary>Cheap pre-parse of the list JSON: the identifiers the caller needs BEFORE it can fetch
        /// the army books Import requires (each distinct armyId, in first-appearance order).</summary>
        public static OprListHeader Peek(string listJson)
        {
            OprList list = Parse(listJson, warn: null);
            return new OprListHeader(
                OprBookImporter.AsciiFold(list.Name ?? string.Empty),
                list.GameSystem ?? string.Empty,
                DistinctArmyIds(list));
        }

        public static OprListImportResult Import(string listJson, IReadOnlyDictionary<string, string> armyBookJsonById)
        {
            var result = new OprListImportResult();
            Action<string> warn = result.Warnings.Add;

            OprList list = Parse(OprBookImporter.AsciiFoldJsonValues(listJson, warn), warn);

            if (!string.Equals(list.GameSystem, SupportedGameSystem, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"This list is for OPR game system '{list.GameSystem ?? "?"}'; " +
                    $"only Grimdark Future ('{SupportedGameSystem}') is supported.");
            }

            // Version gate: every referenced book must sit inside the supported major.minor.
            var books = new Dictionary<string, BookHeader>();
            foreach (string armyId in DistinctArmyIds(list))
            {
                if (!armyBookJsonById.TryGetValue(armyId, out string? bookJson))
                    throw new InvalidOperationException($"No army-book JSON was supplied for armyId '{armyId}'.");
                BookHeader header = ParseBookHeader(bookJson);
                if (!VersionMatches(header.Version))
                    throw new OprVersionMismatchException(header.Name, header.Version);
                books[armyId] = header;
            }

            var army = new ArmyListFile
            {
                Name = string.IsNullOrWhiteSpace(list.Name) ? "Imported Army" : list.Name!,
                PointsLimit = list.PointsLimit ?? 0,
                Faction = PrimaryFaction(list, books, warn),
            };

            // Map every entry 1:1 first (combined halves included), then fold the pairs — mirrors
            // ListCompiler.Compile + MergeCombinedUnits so an imported combined unit is shaped identically
            // to a Forge-built one.
            var mapped = new List<(OprListUnit Dto, UnitFileEntry Entry)>();
            foreach (OprListUnit unit in list.Units ?? new())
            {
                string bookName = unit.ArmyId is not null && books.TryGetValue(unit.ArmyId, out BookHeader? h)
                    ? h.Name : string.Empty;
                mapped.Add((unit, MapUnit(unit, bookName, warn)));
            }
            MergeCombinedUnits(mapped, warn);
            army.Units.AddRange(mapped.Select(m => m.Entry));

            // A hero's join target must exist after the merge; a dangling reference deploys the hero solo.
            foreach (UnitFileEntry entry in army.Units)
            {
                if (entry.JoinsUnitId is null) continue;
                if (army.Units.Any(u => u != entry && u.Id == entry.JoinsUnitId)) continue;
                warn($"'{entry.Name}' joins a unit that is not in the list (id '{entry.JoinsUnitId}') - it will deploy on its own.");
                entry.JoinsUnitId = null;
            }

            // Per-unit costs are BASE costs; listPoints is the only resolved total. Park the difference so
            // TotalPoints (and force-org validation with it) agrees with Army Forge instead of importing
            // light by every upgrade point.
            if (list.ListPoints is int authoritative && authoritative > 0)
            {
                int attributed = army.Units.Sum(u => u.PointCost);
                army.UnattributedPoints = authoritative - attributed;
                if (army.UnattributedPoints > 0)
                {
                    warn($"{army.UnattributedPoints} of this list's {authoritative} pts are upgrade points " +
                         "Army Forge does not publish per unit - they are counted in the army total but " +
                         "cannot be shown against the unit that earned them.");
                }
                else if (army.UnattributedPoints < 0)
                {
                    warn($"Army Forge reports {authoritative} pts but the units sum to {attributed} pts - " +
                         "importing at the Army Forge total; per-unit costs may be unreliable.");
                }
            }
            else
            {
                warn("This list carries no 'listPoints' total - the army total is a sum of BASE unit costs " +
                     "and will read low if any unit has upgrades.");
            }

            if (army.Units.Count == 0)
                warn("The list contains no units.");
            if (list.CampaignMode)
                warn("Campaign mode is not imported (#242) - campaign rules are ignored.");
            if (list.NarrativeMode)
                warn("Narrative mode is not imported (#242).");

            result.ListErrors.AddRange(list.ForceOrgErrors ?? new());

            // #239 effect sets: same treatment a Forge-compiled or retrofitted army gets — faction
            // defaults plus keyword-baked per-weapon keys (imports carry no explicit keys of their own).
            WeaponEffectAssigner.ApplyToArmy(army);

            result.Army = army;
            return result;
        }

        /// <summary>Copies a bundled book's rule/spell definitions onto the imported army — the same carry
        /// ListCompiler.Compile does for Forge-built armies — so faction rules (and Caster spell lists)
        /// resolve at load instead of riding as inert name references.</summary>
        public static void AttachBookDefinitions(ArmyListFile army, BookFile book)
        {
            army.RuleDefinitions = new List<SpecialRuleDefinition>(book.RuleDefinitions);
            army.Spells = new List<SpellDefinition>(book.Spells);
        }

        /// <summary>The distinct rule names on this army (unit + weapon scope) that will NOT resolve at
        /// load — the engine warn-and-skips them, so they are inert in play. Mirrors army-load's registry:
        /// the core catalog first, then the army's embedded definitions override by name.</summary>
        public static IReadOnlyList<string> UnresolvedRuleNames(ArmyListFile army)
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in army.RuleDefinitions)
                resolver.RegisterOrReplace(definition);

            var unresolved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            void Check(IEnumerable<SpecialRuleEntry> rules)
            {
                foreach (SpecialRuleEntry rule in rules)
                    if (!resolver.TryResolve(ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName, out _))
                        unresolved.Add(rule.PrintableName);
            }

            foreach (UnitFileEntry unit in army.Units)
            {
                Check(unit.SpecialRules);
                foreach (WeaponFileEntry weapon in unit.Weapons) Check(weapon.SpecialRules);
            }
            return unresolved.ToList();
        }

        // ── Parsing ──────────────────────────────────────────────────────────────────────────────────────

        private static OprList Parse(string listJson, Action<string>? warn)
        {
            try
            {
                return JsonSerializer.Deserialize<OprList>(listJson, OprBookImporter.ReadOpts)
                    ?? throw new InvalidOperationException("OPR list JSON did not deserialize.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Malformed OPR list JSON: {ex.Message}", ex);
            }
        }

        private static BookHeader ParseBookHeader(string bookJson)
        {
            try
            {
                OprBookHeaderDto dto = JsonSerializer.Deserialize<OprBookHeaderDto>(bookJson, OprBookImporter.ReadOpts)
                    ?? throw new InvalidOperationException("OPR army-book JSON did not deserialize.");
                return new BookHeader(
                    OprBookImporter.AsciiFold(dto.Name ?? "Unknown Book"),
                    dto.VersionString ?? string.Empty);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Malformed OPR army-book JSON: {ex.Message}", ex);
            }
        }

        private static IReadOnlyList<string> DistinctArmyIds(OprList list) =>
            (list.Units ?? new()).Select(u => u.ArmyId).OfType<string>().Distinct().ToList();

        // "3.5.3" matches prefix "3.5"; "3.5" itself matches; "3.50.1" must NOT (component compare, not
        // string StartsWith). OPR's live books legitimately mix patch levels (3.5.2/3.5.3 today).
        internal static bool VersionMatches(string versionString)
        {
            string[] found = versionString.Split('.');
            string[] supported = SupportedVersionPrefix.Split('.');
            if (found.Length < supported.Length) return false;
            for (int i = 0; i < supported.Length; i++)
                if (!string.Equals(found[i].Trim(), supported[i], StringComparison.Ordinal)) return false;
            return true;
        }

        // ── Mapping ──────────────────────────────────────────────────────────────────────────────────────

        // Faction label: the book most of the list's units come from; extra books are disclosed, not dropped.
        private static string PrimaryFaction(OprList list, IReadOnlyDictionary<string, BookHeader> books,
            Action<string> warn)
        {
            if (books.Count == 0) return string.Empty;
            string primaryId = (list.Units ?? new()).Where(u => u.ArmyId is not null)
                .GroupBy(u => u.ArmyId!).OrderByDescending(g => g.Count()).First().Key;
            string primary = books[primaryId].Name;
            if (books.Count > 1)
            {
                warn("List mixes units from multiple army books (" +
                    string.Join(", ", books.Values.Select(b => b.Name)) + $") - faction is recorded as '{primary}'.");
            }
            return primary;
        }

        private static UnitFileEntry MapUnit(OprListUnit unit, string bookName, Action<string> warn)
        {
            string name = string.IsNullOrWhiteSpace(unit.CustomName) ? (unit.Name ?? "Unit") : unit.CustomName!;
            var entry = new UnitFileEntry
            {
                Name = name,
                Id = unit.SelectionId,
                JoinsUnitId = unit.JoinToUnit,
                ModelCount = Math.Max(1, unit.Size),
                Quality = unit.Quality,
                Defense = unit.Defense,
                PointCost = unit.Cost,
                Base = OprBookImporter.MapBase(unit.Bases),
            };

            foreach (OprListRule rule in unit.Rules ?? new())
                AddRule(entry, MapRule(rule, bookName));

            // `loadout` is the unit's FINAL gear as Army Forge resolved it (upgrades applied, counts are
            // unit totals). Only a list shape without one falls back to the base weapons + items.
            List<OprLoadoutEntry> gear = unit.Loadout is { Count: > 0 }
                ? unit.Loadout
                : (unit.Weapons ?? new()).Concat(unit.Items ?? new()).ToList();
            foreach (OprLoadoutEntry item in gear)
                AddLoadoutEntry(entry, item, bookName, warn);

            // Upgrades granting bare RULES may or may not already be reflected in `rules` (unverified
            // corpus corner, see #241) — dedupe-merge covers both. Weapons/items are trusted to `loadout`.
            foreach (OprSelectedUpgrade selected in unit.SelectedUpgrades ?? new())
                foreach (OprLoadoutEntry gain in selected.Option?.Gains ?? new())
                {
                    if (gain.Type == "ArmyBookRule")
                        AddRule(entry, MapRule(new OprListRule { Name = gain.Name, Rating = gain.Rating }, bookName));
                    else if (gain.Type == "ArmyBookItem")
                        foreach (OprLoadoutEntry inner in gain.Content ?? new())
                            if (inner.Type == "ArmyBookRule")
                                AddRule(entry, MapRule(new OprListRule { Name = inner.Name, Rating = inner.Rating }, bookName));
                }

            if (unit.Xp > 0 || (unit.Traits ?? new()).Count > 0)
                warn($"'{name}': campaign XP/traits are not imported (#242).");

            return entry;
        }

        // A loadout entry is a weapon, or a named item bundling rules (and possibly nested weapons). Item
        // rules fold into the unit's rule list — same as ListCompiler's untargeted-item path; army load
        // spreads weapon-scoped ones across the unit's weapons.
        private static void AddLoadoutEntry(UnitFileEntry entry, OprLoadoutEntry item, string bookName,
            Action<string> warn)
        {
            switch (item.Type)
            {
                case "ArmyBookWeapon":
                    ListCompiler.AddWeapon(entry.Weapons, MapWeapon(item, bookName, warn), applications: 1);
                    break;
                case "ArmyBookItem":
                    foreach (OprLoadoutEntry inner in item.Content ?? new())
                    {
                        if (inner.Type == "ArmyBookRule")
                            AddRule(entry, MapRule(new OprListRule { Name = inner.Name, Rating = inner.Rating }, bookName));
                        else
                            AddLoadoutEntry(entry, inner, bookName, warn);
                    }
                    break;
                case "ArmyBookRule":
                    AddRule(entry, MapRule(new OprListRule { Name = item.Name, Rating = item.Rating }, bookName));
                    break;
                default:
                    warn($"'{entry.Name}': loadout entry '{item.Name ?? "?"}' has unknown type '{item.Type ?? "?"}' - skipped.");
                    break;
            }
        }

        private static WeaponFileEntry MapWeapon(OprLoadoutEntry w, string bookName, Action<string> warn)
        {
            var weapon = new WeaponFileEntry
            {
                Name = w.Name ?? "Weapon",
                Quantity = Math.Max(1, w.Count ?? 1),
                RangeInches = w.Range ?? 0,
                Attacks = w.Attacks ?? 0,
            };
            if (w.AttacksMultiplier is int m && m != 1)
                warn($"Weapon '{weapon.Name}' has attacksMultiplier {m} (unsupported) - attacks left at {weapon.Attacks}.");
            foreach (OprListRule rule in w.SpecialRules ?? new())
            {
                if (rule.Name == "AP" && rule.Rating is int ap) weapon.ArmorPenetration = ap;
                else weapon.SpecialRules.Add(MapRule(rule, bookName));
            }
            return weapon;
        }

        private static SpecialRuleEntry MapRule(OprListRule rule, string bookName)
        {
            SpecialRuleEntry entry = rule.Rating is int rating
                ? new SpecialRuleEntry_CoreNumeric(rule.Name ?? "Rule", rating)
                : new SpecialRuleEntry_Core(rule.Name ?? "Rule");
            // Same army-context disambiguation the book importer applies (#197 Darkborn).
            return OprBookImporter.Disambiguate(bookName, entry);
        }

        private static void AddRule(UnitFileEntry entry, SpecialRuleEntry rule)
        {
            if (!entry.SpecialRules.Contains(rule)) entry.SpecialRules.Add(rule);
        }

        // #107-shaped combined squads arrive as TWO entries sharing the roster unit `id`, both flagged
        // `combined`, the absorbed half's joinToUnit naming the host's selectionId. Fold them the way
        // ListCompiler.MergeCombinedUnits does, so play sees one big unit.
        private static void MergeCombinedUnits(List<(OprListUnit Dto, UnitFileEntry Entry)> mapped,
            Action<string> warn)
        {
            for (int i = mapped.Count - 1; i >= 0; i--)
            {
                (OprListUnit dto, UnitFileEntry absorbed) = mapped[i];
                if (!dto.Combined || string.IsNullOrEmpty(dto.JoinToUnit)) continue;

                int hostIndex = mapped.FindIndex(m => m.Entry != absorbed
                    && m.Dto.Combined && m.Dto.SelectionId == dto.JoinToUnit && m.Dto.Id == dto.Id);
                if (hostIndex < 0)
                {
                    warn($"'{absorbed.Name}' is flagged combined but its partner was not found - imported as its own unit.");
                    absorbed.JoinsUnitId = null; // not a hero join; don't leave a dangling reference
                    continue;
                }

                UnitFileEntry host = mapped[hostIndex].Entry;
                host.ModelCount += absorbed.ModelCount;
                host.PointCost += absorbed.PointCost;
                foreach (WeaponFileEntry weapon in absorbed.Weapons)
                    ListCompiler.AddWeapon(host.Weapons, weapon, applications: 1);
                foreach (SpecialRuleEntry rule in absorbed.SpecialRules)
                    if (!host.SpecialRules.Contains(rule))
                        host.SpecialRules.Add(rule);
                if (!host.Name.EndsWith(" (Combined)", StringComparison.Ordinal))
                    host.Name += " (Combined)";

                mapped.RemoveAt(i);

                // A hero joined to the absorbed half follows it into the merged unit.
                if (!string.IsNullOrEmpty(absorbed.Id))
                    foreach ((_, UnitFileEntry other) in mapped)
                        if (other.JoinsUnitId == absorbed.Id)
                            other.JoinsUnitId = host.Id;
            }
        }

        // ── OPR list JSON DTOs (only the fields we consume) ─────────────────────────────────────────────

        private sealed class OprList
        {
            public string? Name { get; set; }
            public string? GameSystem { get; set; }
            public int? PointsLimit { get; set; }
            /// <summary>Army Forge's own authoritative total for the list. The ONLY resolved points figure
            /// in the payload - per-unit `cost` is a base cost (#241, 2026-07-19).</summary>
            public int? ListPoints { get; set; }
            public bool CampaignMode { get; set; }
            public bool NarrativeMode { get; set; }
            public List<OprListUnit>? Units { get; set; }
            public List<string>? ForceOrgErrors { get; set; }
        }

        private sealed class OprListUnit
        {
            public string? Id { get; set; }             // roster/book unit id — shared by combined halves
            public string? Name { get; set; }
            public string? CustomName { get; set; }
            public int Size { get; set; }
            /// <summary>The unit's BASE cost, NOT its resolved cost with upgrades - verified 2026-07-19
            /// against OPR's own book API, where the same figure appears as the unit's catalog price.
            /// Never treat this as a final per-unit total (see ArmyListFile.UnattributedPoints).</summary>
            public int Cost { get; set; }
            public int Quality { get; set; }
            public int Defense { get; set; }
            public OprBookImporter.OprBases? Bases { get; set; }
            public List<OprListRule>? Rules { get; set; }
            public List<OprLoadoutEntry>? Weapons { get; set; }
            public List<OprLoadoutEntry>? Items { get; set; }
            public List<OprLoadoutEntry>? Loadout { get; set; }
            public string? ArmyId { get; set; }
            public int Xp { get; set; }
            public List<string?>? Traits { get; set; }
            public bool Combined { get; set; }
            public string? JoinToUnit { get; set; }     // host selectionId (hero join or combined partner)
            public string? SelectionId { get; set; }    // this entry's per-list handle
            public List<OprSelectedUpgrade>? SelectedUpgrades { get; set; }
        }

        private sealed class OprListRule
        {
            public string? Name { get; set; }
            public int? Rating { get; set; }
        }

        private sealed class OprLoadoutEntry
        {
            public string? Type { get; set; }           // ArmyBookWeapon | ArmyBookItem | ArmyBookRule
            public string? Name { get; set; }
            public int? Count { get; set; }
            public int? Range { get; set; }
            public int? Attacks { get; set; }
            public int? AttacksMultiplier { get; set; }
            public int? Rating { get; set; }            // rule-typed entries
            public List<OprListRule>? SpecialRules { get; set; }
            public List<OprLoadoutEntry>? Content { get; set; }
        }

        private sealed class OprSelectedUpgrade
        {
            public OprSelectedRef? Upgrade { get; set; }
            public OprSelectedOption? Option { get; set; }
        }

        // The selectedUpgrades shape is the corpus's least-verified corner (#241): every field here is
        // optional and ReconstructSelections walks a defensive matching ladder over whatever is present.
        private sealed class OprSelectedRef
        {
            public string? Id { get; set; }
            public string? Uid { get; set; }
            public string? SectionId { get; set; }
        }

        private sealed class OprSelectedOption
        {
            public string? Id { get; set; }
            public string? Uid { get; set; }
            public string? Label { get; set; }
            public List<OprLoadoutEntry>? Gains { get; set; }
        }

        private sealed class OprBookHeaderDto
        {
            public string? Name { get; set; }
            public string? VersionString { get; set; }
        }

        private sealed record BookHeader(string Name, string Version);
    }

    /// <summary>What <see cref="OprListImporter.Import"/> produced: the playable army plus everything the
    /// preview should disclose. <see cref="Warnings"/> are OUR fidelity notes (ignored features, unknown
    /// shapes); <see cref="ListErrors"/> are Army Forge's own validation complaints about the list
    /// (e.g. over the points limit) — informational, they don't block the import.</summary>
    public sealed class OprListImportResult
    {
        public ArmyListFile Army { get; set; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> ListErrors { get; } = new();
    }

    /// <summary>Identifiers pre-parsed from a share list before import (see <see cref="OprListImporter.Peek"/>):
    /// the caller fetches one army book per <see cref="ArmyIds"/> entry, then calls Import.</summary>
    public sealed record OprListHeader(string Name, string GameSystem, IReadOnlyList<string> ArmyIds);

    /// <summary>The list references an army book whose current version is outside the supported
    /// major.minor (<see cref="OprListImporter.SupportedVersionPrefix"/>). OPR does not keep old book
    /// versions available once they update, so the import refuses rather than mixing rule generations —
    /// resolving this needs a game-data update, not a different list.</summary>
    public sealed class OprVersionMismatchException : Exception
    {
        public string BookName { get; }
        public string FoundVersion { get; }

        public OprVersionMismatchException(string bookName, string foundVersion)
            : base($"Army book '{bookName}' is at OPR version {(string.IsNullOrEmpty(foundVersion) ? "(unknown)" : foundVersion)}; " +
                   $"this game supports {OprListImporter.SupportedVersionPrefix}.x. " +
                   "The game's data needs an update before this list can be imported.")
        {
            BookName = bookName;
            FoundVersion = foundVersion;
        }
    }
}
