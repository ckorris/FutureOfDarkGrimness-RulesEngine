using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #153 (P0b) — one-time importer: OnePageRules Army Forge JSON (/api/army-books/<uid>) → our BookFile.
    // Captures FUNCTIONAL game data only (unit stats, weapons, point costs, upgrade options, rule names). It
    // never reads the book's background/lore prose. Imported data is OPR's, used under CC-BY-SA — Import stamps
    // Source + License on the book. Special rules import as name references; the engine already skips rules it
    // doesn't implement (so unimplemented OPR rules are inert, not errors). The importer emits RuleDefinitions
    // only for spell-synthesized effects (see the Spells section); curated definitions for faction rules come
    // from a rule-supplement file merged by BookRuleSupplement (Apply here via the optional import argument).
    //
    // Fidelity notes (v1 — recorded, not silently dropped):
    //   • affects "exactly N" / "up to N" → mapped to Any (user picks the count); the exact/max bound is not
    //     yet enforced (P4 validation).
    //   • Model-count upgrades (combined units / "+N models") are not synthesized as AddModels — units import
    //     at their base size.
    public static class OprBookImporter
    {
        // OPR's JSON types numeric fields loosely — a rating/count can arrive as a number OR a string (and a
        // string field like a base size could plausibly arrive as a bare number). Tolerant converters keep the
        // import from throwing on that. Shared with OprListImporter (#241) — same corpus, same looseness.
        internal static readonly JsonSerializerOptions ReadOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new LooseIntConverter(), new LooseNullableIntConverter(), new LooseStringConverter() },
        };

        private sealed class LooseIntConverter : JsonConverter<int>
        {
            public override int Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => ReadLooseInt(ref r) ?? 0;
            public override void Write(Utf8JsonWriter w, int v, JsonSerializerOptions o) => w.WriteNumberValue(v);
        }

        private sealed class LooseNullableIntConverter : JsonConverter<int?>
        {
            public override int? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => ReadLooseInt(ref r);
            public override void Write(Utf8JsonWriter w, int? v, JsonSerializerOptions o)
            {
                if (v.HasValue) w.WriteNumberValue(v.Value); else w.WriteNullValue();
            }
        }

        private static int? ReadLooseInt(ref Utf8JsonReader r) => r.TokenType switch
        {
            JsonTokenType.Number => r.TryGetInt32(out int i) ? i : (int)r.GetDouble(),
            JsonTokenType.String => int.TryParse(r.GetString(), out int v) ? v : null,
            _ => null,
        };

        private sealed class LooseStringConverter : JsonConverter<string?>
        {
            public override string? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
            {
                switch (r.TokenType)
                {
                    case JsonTokenType.String: return r.GetString();
                    case JsonTokenType.Number: return r.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case JsonTokenType.True: return "true";
                    case JsonTokenType.False: return "false";
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        r.Skip(); return null;
                    default: return null;
                }
            }

            public override void Write(Utf8JsonWriter w, string? v, JsonSerializerOptions o)
            {
                if (v is null) w.WriteNullValue(); else w.WriteStringValue(v);
            }
        }

        public static BookFile Import(string oprJson, string source, string license, Action<string>? warn = null)
        {
            // Game text is ASCII-only (see CLAUDE.md) — the app's font atlas bakes no glyphs beyond Latin-1,
            // so OPR's typographic characters (em-dashes, curly quotes, macrons like the i-macron in some
            // Alien Hives text) would render as '?'. Fold every string VALUE in the document up front so all
            // imported text lands ASCII. Value-by-value via JsonNode, NOT on the raw text: folding a curly
            // double quote to '"' inside raw JSON would terminate the string and corrupt the document.
            OprBook opr;
            try
            {
                oprJson = AsciiFoldJsonValues(oprJson, warn);
                opr = JsonSerializer.Deserialize<OprBook>(oprJson, ReadOpts)
                    ?? throw new InvalidOperationException("OPR army-book JSON did not deserialize.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Malformed OPR army-book JSON: {ex.Message}", ex);
            }

            var packages = (opr.UpgradePackages ?? new()).Where(p => p.Uid is not null)
                .ToDictionary(p => p.Uid!, p => p);

            var book = new BookFile
            {
                Name = opr.Name ?? "Imported Book",
                Faction = opr.Name ?? string.Empty,
                Version = opr.VersionString is null ? string.Empty : $"OPR v{opr.VersionString}",
                Source = source,
                License = license,
            };

            // #239: stamp the faction's default effect sets so armies forged from this book inherit
            // them (no-op for a faction the assigner's table doesn't know).
            WeaponEffectAssigner.ApplyToBook(book);

            foreach (OprUnit unit in opr.Units ?? new())
                book.Units.Add(MapUnit(unit, packages, warn));

            DisambiguateAmbiguousRuleNames(book);

            foreach (OprSpell spell in opr.Spells ?? new())
            {
                SpellDefinition? parsed = TryParseSpell(spell, out SpecialRuleDefinition? synthesized);
                if (parsed is null)
                {
                    warn?.Invoke($"Spell '{spell.Name}' skipped: unrecognized effect text \"{spell.Effect}\".");
                    continue;
                }

                book.Spells.Add(parsed);
                if (synthesized is not null && !book.RuleDefinitions.Any(d =>
                        string.Equals(d.Name, synthesized.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    book.RuleDefinitions.Add(synthesized);
                }
            }

            return book;
        }

        /// <summary>Parses the JSON and ASCII-folds every string value (leaves, not property names — those
        /// are ASCII schema keys), then re-serializes. The serializer re-escapes any quote the fold
        /// introduced, so the document stays well-formed.</summary>
        internal static string AsciiFoldJsonValues(string json, Action<string>? warn = null)
        {
            if (json.All(c => c <= 127)) return json;

            System.Text.Json.Nodes.JsonNode? root = System.Text.Json.Nodes.JsonNode.Parse(json);
            if (root is null) return json;
            FoldNode(root, warn);
            return root.ToJsonString();
        }

        private static void FoldNode(System.Text.Json.Nodes.JsonNode node, Action<string>? warn)
        {
            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject obj:
                    foreach (var pair in obj.ToList())
                    {
                        if (pair.Value is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? s))
                            obj[pair.Key] = AsciiFold(s, warn);
                        else if (pair.Value is not null)
                            FoldNode(pair.Value, warn);
                    }
                    break;
                case System.Text.Json.Nodes.JsonArray arr:
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? s))
                            arr[i] = AsciiFold(s, warn);
                        else if (arr[i] is not null)
                            FoldNode(arr[i]!, warn);
                    }
                    break;
            }
        }

        /// <summary>
        /// Folds text to ASCII: typographic punctuation maps to its plain equivalent, accented letters lose
        /// their diacritics via NFD decomposition (i-macron -> i). Anything still non-ASCII after both passes
        /// is kept as-is and reported through <paramref name="warn"/> — never silently mangled.
        /// </summary>
        internal static string AsciiFold(string text, Action<string>? warn = null)
        {
            if (text.All(c => c <= 127)) return text;

            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text.Normalize(System.Text.NormalizationForm.FormD))
            {
                if (c <= 127) { sb.Append(c); continue; }
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) ==
                    System.Globalization.UnicodeCategory.NonSpacingMark) continue; // combining accent from NFD
                sb.Append(c switch
                {
                    '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => "-",
                    '‘' or '’' or 'ʼ' => "'",
                    '“' or '”' => "\"",
                    '…' => "...",
                    ' ' => " ",  // no-break space
                    '×' => "x",  // multiplication sign
                    '→' => "->",
                    '≤' => "<=",
                    '≥' => ">=",
                    _ => c.ToString(),
                });
            }

            string folded = sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
            char[] leftovers = folded.Where(c => c > 127).Distinct().ToArray();
            if (leftovers.Length > 0)
            {
                warn?.Invoke("Non-ASCII characters survived the import fold (will render as '?' in-game): " +
                    string.Join(", ", leftovers.Select(c => $"U+{(int)c:X4} '{c}'")));
            }
            return folded;
        }

        private static RosterUnit MapUnit(OprUnit unit, IReadOnlyDictionary<string, OprPackage> packages,
            Action<string>? warn = null)
        {
            // Mapped up front: #225 defect B estimates a missing base from the unit's rules (Hero + Tough).
            List<SpecialRuleEntry> rules = (unit.Rules ?? new()).Select(MapRule).ToList();

            var roster = new RosterUnit
            {
                Id = unit.Id ?? Guid.NewGuid().ToString("N")[..8],
                Name = unit.Name ?? "Unit",
                Quality = unit.Quality,
                Defense = unit.Defense,
                BaseModelCount = Math.Max(1, unit.Size),
                MinModels = Math.Max(1, unit.Size),
                MaxModels = Math.Max(1, unit.Size),
                BasePointCost = unit.Cost,
                Base = MapBase(unit.Bases, rules, unit.Name, warn),
                Weapons = (unit.Weapons ?? new()).Select(MapWeapon).ToList(),
                Rules = rules,
                Items = (unit.Items ?? new()).Select(MapItem).ToList(),
            };

            // Resolve the unit's referenced upgrade packages → their sections.
            foreach (string pkgUid in unit.Upgrades ?? new())
                if (packages.TryGetValue(pkgUid, out OprPackage? pkg))
                    foreach (OprSection section in pkg.Sections ?? new())
                        roster.Sections.Add(MapSection(section, roster.Id));

            return roster;
        }

        private static WeaponFileEntry MapWeapon(OprWeapon w)
        {
            var weapon = new WeaponFileEntry
            {
                Name = w.Name ?? "Weapon",
                Quantity = Math.Max(1, w.Count ?? 1),
                RangeInches = w.Range ?? 0,
                Attacks = w.Attacks ?? 0,
            };
            ApplyRules(weapon, w.SpecialRules);
            return weapon;
        }

        // AP folds into the numeric ArmorPenetration field; every other rule rides SpecialRules.
        private static void ApplyRules(WeaponFileEntry weapon, List<OprRule>? rules)
        {
            foreach (OprRule r in rules ?? new())
            {
                if (r.Name == "AP" && r.Rating is int ap) weapon.ArmorPenetration = ap;
                else weapon.SpecialRules.Add(MapRule(r));
            }
        }

        private static SpecialRuleEntry MapRule(OprRule r) =>
            r.Rating is int rating
                ? new SpecialRuleEntry_CoreNumeric(r.Name ?? "Rule", rating)
                : new SpecialRuleEntry_Core(r.Name ?? "Rule");

        // #197 (Darkborn): OPR reuses the bare name "Darkborn" for two mechanically different rules across
        // armies -- Dark Brothers' is the defensive debuff (enemies get -4" range / -2" charge vs this unit),
        // Dark Prime Brothers' is the offensive self-buff (+3" range / +3" charge). The catalog registers them
        // under disambiguated names ("Darkborn (Defensive)" / "Darkborn (Offensive)"); the bare name resolves
        // to neither, so all references were dead. Only the importer has the owning-army context needed to
        // route the bare name to the right variant, so it does the disambiguation once, at import time, keyed
        // on the OPR army name -- a re-import stays correct rather than reintroducing the dead bare name.
        private static readonly Dictionary<(string Army, string Rule), string> AmbiguousRuleNames = new()
        {
            [("Dark Brothers", "Darkborn")] = "Darkborn (Defensive)",
            [("Dark Prime Brothers", "Darkborn")] = "Darkborn (Offensive)",
        };

        private static void DisambiguateAmbiguousRuleNames(BookFile book)
        {
            void Fix(List<SpecialRuleEntry> rules)
            {
                for (int i = 0; i < rules.Count; i++)
                    rules[i] = Disambiguate(book.Name, rules[i]);
            }

            foreach (RosterUnit unit in book.Units)
            {
                Fix(unit.Rules);
                foreach (WeaponFileEntry w in unit.Weapons) Fix(w.SpecialRules);
                foreach (ItemEntry item in unit.Items) Fix(item.Rules);
                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                    {
                        Fix(option.RulesGained);
                        foreach (WeaponFileEntry w in option.WeaponsGained) Fix(w.SpecialRules);
                        foreach (ItemEntry item in option.ItemsGained) Fix(item.Rules);
                    }
            }
        }

        internal static SpecialRuleEntry Disambiguate(string armyName, SpecialRuleEntry entry)
        {
            string? name = entry switch
            {
                SpecialRuleEntry_Core core => core.Name,
                SpecialRuleEntry_CoreNumeric numeric => numeric.Name,
                _ => null,
            };
            if (name is null || !AmbiguousRuleNames.TryGetValue((armyName, name), out string? canonical))
                return entry;

            return entry switch
            {
                SpecialRuleEntry_CoreNumeric numeric => new SpecialRuleEntry_CoreNumeric(canonical, numeric.NumericValue),
                _ => new SpecialRuleEntry_Core(canonical),
            };
        }

        private static UpgradeSection MapSection(OprSection s, string unitId)
        {
            // OPR affects: "all" (every matched target), "any" (0..present), "exactly N"/"up to N" (bounded),
            // or null ("replace one" = up to 1). A bound of 1 collapses to the One toggle; >1 is a capped stepper.
            string? type = s.Affects?.Type;
            int value = s.Affects?.Value ?? 0;
            UpgradeAffects affects;
            int maxApplications = 0;
            switch (type)
            {
                case "all": affects = UpgradeAffects.All; break;
                case "any": affects = UpgradeAffects.Any; break;
                case "exactly" or "up to":
                    if (value <= 1) affects = UpgradeAffects.One;
                    else { affects = UpgradeAffects.Any; maxApplications = value; }
                    break;
                default: affects = UpgradeAffects.One; break;
            }

            // OPR select: the PICK bound — "Upgrade with one:" ({exactly,1}), "up to two", or "any" (every
            // option may be taken). Distinct from affects (how many MODELS an application touches). Absent →
            // one pick. Corpus-wide, non-"exactly 1" selects appear on upgrade/attachment sections and on
            // counted replace sections (where the stepper, not MaxPicks, is the bound — harmless either way).
            int maxPicks = s.Select?.Type switch
            {
                "any" => Math.Max(1, (s.Options ?? new()).Count),
                "exactly" or "up to" => Math.Max(1, s.Select?.Value ?? 1),
                _ => 1,
            };

            return new UpgradeSection
            {
                Id = s.Id ?? s.Uid ?? Guid.NewGuid().ToString("N")[..8],
                Label = s.Label ?? string.Empty,
                // "attachment" ("Take one X attachment") is additive like upgrade — only "replace" swaps.
                Variant = s.Variant == "replace" ? UpgradeVariant.Replace : UpgradeVariant.Upgrade,
                Affects = affects,
                MaxApplications = maxApplications,
                MaxPicks = maxPicks,
                Targets = s.Targets ?? new(),
                Options = (s.Options ?? new()).Select(o => MapOption(o, unitId)).ToList(),
            };
        }

        private static UpgradeOption MapOption(OprOption o, string unitId)
        {
            // #219: prefer the flat scalar; fall back to this unit's entry in the per-unit `costs` array.
            // Only genuinely unpriced (neither present) stays CostUnpriced.
            int? resolvedCost = o.Cost ?? o.Costs?.FirstOrDefault(c => c.UnitId == unitId)?.Cost;
            var option = new UpgradeOption
            {
                Id = o.Id ?? o.Uid ?? Guid.NewGuid().ToString("N")[..8],
                Label = o.Label ?? string.Empty,
                Cost = resolvedCost ?? 0,
                CostUnpriced = resolvedCost is null,
            };
            foreach (OprGain g in o.Gains ?? new())
                AddGain(option, g);
            return option;
        }

        // A gain is a weapon, a rule, or a named item bundling rules (kept as an ItemEntry so the name survives
        // for display and Replace targeting; any weapon nested in an item's content is routed to WeaponsGained).
        private static void AddGain(UpgradeOption option, OprGain g)
        {
            switch (g.Type)
            {
                case "ArmyBookWeapon":
                    var weapon = new WeaponFileEntry
                    {
                        Name = g.Name ?? "Weapon",
                        Quantity = Math.Max(1, g.Count ?? 1),
                        RangeInches = g.Range ?? 0,
                        Attacks = g.Attacks ?? 0,
                    };
                    ApplyRules(weapon, g.SpecialRules);
                    option.WeaponsGained.Add(weapon);
                    break;
                case "ArmyBookRule":
                    option.RulesGained.Add(MapRule(new OprRule { Name = g.Name, Rating = g.Rating }));
                    break;
                case "ArmyBookItem":
                    var item = MapItem(g);
                    foreach (OprGain inner in (g.Content ?? new()).Where(c => c.Type == "ArmyBookWeapon"))
                        AddGain(option, inner);
                    option.ItemsGained.Add(item);
                    break;
            }
        }

        // ── Spells ───────────────────────────────────────────────────────────────────────────────────────
        // OPR spell effects are formulaic prose; the corpus shapes map onto the #033/#034 vocabulary:
        //   "…which takes 4 hits with AP(2)."                                      → Effect.DealHits
        //   "…which get X once (next time the effect would apply)."                → Effect.AddRule (NextTrigger)
        //   "…which friendly units get X against once…"                            → Effect.MarkTarget
        //   "…which must take a morale test. If failed, it becomes fatigued."      → Effect.MoraleTestThen(ApplyFatigue)
        //   "…must take a morale test. If failed you may move it by up to 6\"…"    → Effect.MoraleTestThen(TriggeredMove)
        //   "…which moves +2\" when using Advance actions and +4\" when using      → AddRule of a SYNTHESIZED
        //    Rush/Charge actions once…" / "…which loses AP(1) when shooting once…"   book-embedded rule (below)
        // A "model" target maps to TargetSelector.SingleModel (the "unit of [1]" wording). AP folds into
        // DealHits.ArmorPenetration like weapon import; other rule names ride WithRules and resolve at army
        // load with the usual skip-unknown tolerance. Anything that doesn't match is SKIPPED with a warning —
        // never silently, and never imported half-wrong.
        //
        // Synthesized rules: the movement/AP shapes have no core rule to grant, so the importer emits a
        // "<Spell Name> Effect" SpecialRuleDefinition into book.RuleDefinitions (the #059 embedded channel —
        // regenerated with the book, so nothing to hand-maintain) and the spell grants it by name with
        // NextTrigger, exactly like the grant family. Each synthesized definition is RuleValidator-checked
        // here; a violation skips the spell rather than shipping a book that fails the load gate.

        private static readonly Regex SpellShape = new(
            "^pick (?<count>one|two|three|up to two|up to three) (?<aff>enemy|friendly) (?<kind>units?|models?) within (?<range>\\d+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DamageShape = new(
            "which takes? (?<hits>\\d+) hits?(?: with (?<rules>[^.]+))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex GrantShape = new(
            "which gets? (?<rule>.+?) once",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "...which friendly units get X against once" — the mark family (#034): the rule lands on friendlies
        // ATTACKING the picked target, i.e. Effect.MarkTarget.
        private static readonly Regex MarkShape = new(
            "which friendly units gets? (?<rule>.+?) against",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "...which moves +2\" when using Advance actions and +4\" when using Rush/Charge actions once" —
        // Shock Speed / Deceleration Rune. No core rule carries arbitrary signed bonuses, so this synthesizes one.
        private static readonly Regex MoveShape = new(
            "which moves? (?<adv>[+-]?\\d+)\" when using Advance actions and (?<rc>[+-]?\\d+)\" when using Rush/Charge actions once",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "...which loses AP(1) when shooting once" — Corrode Weapons. Synthesizes an attacker-side
        // ReduceArmorPenetration rule (same floored-at-AP(0) primitive Fortified uses defender-side).
        private static readonly Regex ApLossShape = new(
            "which loses AP\\((?<ap>\\d+)\\) when shooting once",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "...which counts as being in Dangerous/Difficult Terrain once" — Cursed Stride / Aura of
        // Pestilence. Synthesizes a counts-as rule (the #153 terrain primitive).
        private static readonly Regex TerrainShape = new(
            "which counts as being in (?<kind>Dangerous|Difficult) Terrain once",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "...which must take a morale test. If failed <consequence>" — the #034 conditional family
        // (Effect.MoraleTestThen). Two corpus consequences: fatigue and a caster-directed move.
        private static readonly Regex MoraleTestShape = new(
            "which must take a morale test\\. If failed(?<tail>[^.]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MoraleFatigueTail = new(
            "^,? it becomes fatigued", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MoraleMoveTail = new(
            "^,? you may move it by up to (?<inches>\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static SpellDefinition? TryParseSpell(OprSpell spell, out SpecialRuleDefinition? synthesized)
        {
            synthesized = null;
            // Trimmed — the corpus has a spell name with a trailing space ("Deceleration Rune "), and the
            // synthesized-rule name derives from it.
            string spellName = (spell.Name ?? "Spell").Trim();
            string text = (spell.Effect ?? string.Empty).Trim();
            Match shape = SpellShape.Match(text);
            if (!shape.Success) return null;

            int range = int.Parse(shape.Groups["range"].Value);
            int maxCount = shape.Groups["count"].Value.ToLowerInvariant() switch
            {
                "two" or "up to two" => 2,
                "three" or "up to three" => 3,
                _ => 1,
            };
            ETargetAffinity affinity = shape.Groups["aff"].Value.StartsWith("f", StringComparison.OrdinalIgnoreCase)
                ? ETargetAffinity.Friend
                : ETargetAffinity.Foe;
            bool singleModel = shape.Groups["kind"].Value.StartsWith("model", StringComparison.OrdinalIgnoreCase);

            Match damage = DamageShape.Match(text);
            if (damage.Success)
            {
                int hits = int.Parse(damage.Groups["hits"].Value);
                int ap = 0;
                var withRules = new List<string>();
                if (damage.Groups["rules"].Success)
                {
                    foreach (string raw in damage.Groups["rules"].Value
                                 .Split(new[] { ", ", " and " }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(r => r.Trim()).Where(r => r.Length > 0))
                    {
                        if (SpecialRuleEntryParser.Parse(raw) is SpecialRuleEntry_CoreNumeric { Name: "AP" } apRule)
                            ap = apRule.NumericValue;
                        else
                            withRules.Add(raw);
                    }
                }
                return new SpellDefinition(spellName, spell.Threshold ?? 1,
                    new TargetSelector(range, 1, maxCount, affinity, RequireLineOfSight: true, SingleModel: singleModel),
                    new Effect.DealHits(hits, withRules, ap));
            }

            // Mark before grant: "which friendly units gets X against once" also contains "gets ... once",
            // but the buff belongs to attackers of the target, not the target itself.
            Match mark = MarkShape.Match(text);
            if (mark.Success)
            {
                return new SpellDefinition(spellName, spell.Threshold ?? 1,
                    new TargetSelector(range, 1, maxCount, affinity, RequireLineOfSight: false),
                    new Effect.MarkTarget(mark.Groups["rule"].Value.Trim()));
            }

            Match move = MoveShape.Match(text);
            if (move.Success)
            {
                int advance = int.Parse(move.Groups["adv"].Value);
                int rushCharge = int.Parse(move.Groups["rc"].Value);
                synthesized = SynthesizeMovementRule(spellName, advance, rushCharge);
                return SynthesizedGrantOrSkip(spell, spellName, range, maxCount, affinity, ref synthesized);
            }

            Match apLoss = ApLossShape.Match(text);
            if (apLoss.Success)
            {
                synthesized = SynthesizeApLossRule(spellName, int.Parse(apLoss.Groups["ap"].Value));
                return SynthesizedGrantOrSkip(spell, spellName, range, maxCount, affinity, ref synthesized);
            }

            Match terrain = TerrainShape.Match(text);
            if (terrain.Success)
            {
                ECountAsTerrain kind = terrain.Groups["kind"].Value.Equals("Dangerous", StringComparison.OrdinalIgnoreCase)
                    ? ECountAsTerrain.Dangerous
                    : ECountAsTerrain.Difficult;
                synthesized = SynthesizeCountAsTerrainRule(spellName, kind);
                return SynthesizedGrantOrSkip(spell, spellName, range, maxCount, affinity, ref synthesized);
            }

            Match moraleTest = MoraleTestShape.Match(text);
            if (moraleTest.Success)
            {
                string tail = moraleTest.Groups["tail"].Value;
                Effect? onFailure =
                    MoraleFatigueTail.IsMatch(tail) ? new Effect.ApplyFatigue()
                    : MoraleMoveTail.Match(tail) is { Success: true } moveTail
                        ? new Effect.TriggeredMove(int.Parse(moveTail.Groups["inches"].Value), IsOptional: true)
                        : null;
                if (onFailure is null) return null; // unknown consequence — skip whole spell, never half-wrong

                return new SpellDefinition(spellName, spell.Threshold ?? 1,
                    new TargetSelector(range, 1, maxCount, affinity, RequireLineOfSight: false),
                    new Effect.MoraleTestThen(onFailure));
            }

            Match grant = GrantShape.Match(text);
            if (grant.Success)
            {
                return new SpellDefinition(spellName, spell.Threshold ?? 1,
                    new TargetSelector(range, 1, maxCount, affinity, RequireLineOfSight: false),
                    new Effect.AddRule(grant.Groups["rule"].Value.Trim(), ELifetime.NextTrigger));
            }

            return null;
        }

        /// <summary>The grant-family spell wrapper for a synthesized rule: the spell grants
        /// "&lt;Spell&gt; Effect" once (NextTrigger), like any other grant spell. If the synthesized
        /// definition fails validation, the whole spell is skipped (null) — never shipped half-wrong.</summary>
        private static SpellDefinition? SynthesizedGrantOrSkip(OprSpell spell, string spellName, int range,
            int maxCount, ETargetAffinity affinity, ref SpecialRuleDefinition? synthesized)
        {
            if (synthesized is null || new Rules.Dispatch.RuleValidator().Validate(synthesized).Count > 0)
            {
                synthesized = null;
                return null;
            }

            return new SpellDefinition(spellName, spell.Threshold ?? 1,
                new TargetSelector(range, 1, maxCount, affinity, RequireLineOfSight: false),
                new Effect.AddRule(synthesized.Name, ELifetime.NextTrigger));
        }

        private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

        private static SpecialRuleDefinition SynthesizeMovementRule(string spellName, int advance, int rushCharge)
        {
            HookEntry Entry(EActionType action, int inches) => new(
                EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(action),
                new Effect.MovementBonus(action, inches),
                ELifetime.ThisActivation);

            return new SpecialRuleDefinition($"{spellName} Effect",
                new[]
                {
                    Entry(EActionType.Advance, advance),
                    Entry(EActionType.Rush, rushCharge),
                    Entry(EActionType.Charge, rushCharge),
                },
                Array.Empty<ActivatedAbility>(),
                Valence: advance >= 0 ? EValence.Positive : EValence.Negative,
                Description: $"Moves {Signed(advance)}\" when using Advance, and {Signed(rushCharge)}\" when using Rush/Charge (from {spellName}).");
        }

        private static SpecialRuleDefinition SynthesizeCountAsTerrainRule(string spellName, ECountAsTerrain kind)
        {
            string consequence = kind == ECountAsTerrain.Dangerous
                ? "each moving model rolls a d6, taking a wound on a 1"
                : $"its move is capped at {FDG.GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES:0}\"";
            return new SpecialRuleDefinition($"{spellName} Effect",
                new[]
                {
                    // Read by MovementRuleQueries.CountsAsInTerrain throughout the move; the one-shot grant
                    // is spent by ExecuteMoveStage when the move resolves.
                    new HookEntry(EHookID.Movement_OnMoveThroughTerrain,
                        new Condition.Always(),
                        new Effect.CountAsInTerrain(kind),
                        ELifetime.ThisActivation),
                },
                Array.Empty<ActivatedAbility>(),
                Valence: EValence.Negative,
                Description: $"This unit counts as being in {kind} Terrain on its next move - {consequence} (from {spellName}).");
        }

        private static SpecialRuleDefinition SynthesizeApLossRule(string spellName, int apLoss)
        {
            return new SpecialRuleDefinition($"{spellName} Effect",
                new[]
                {
                    // Attacker-side AP reduction on the shooter's own weapon, floored at AP(0) by the save
                    // stage (the same primitive Fortified applies defender-side). Not(IsMelee) keeps it to
                    // shooting, per the spell text.
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.Not(new Condition.IsMelee()),
                        new Effect.ReduceArmorPenetration(apLoss),
                        ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>(),
                Valence: EValence.Negative,
                Description: $"This unit's shooting has its AP reduced by {apLoss}, to a minimum of AP(0) (from {spellName}).");
        }

        private const float MmPerInch = 25.4f;

        // OPR bases come as {round, square} in millimetres — "25", "120x92" (an oval), "none", or "". Prefer
        // the round basing (GDF standard): a single number → Circle; "LxW" → our Rectangle footprint (the
        // engine has no oval; #149's rectangle is the dimension-faithful stand-in). Fall back to the square
        // spec, else ESTIMATE one from the unit's rules (#225 defect B).
        //
        // #225: OPR writes the pair LENGTH-first ("60x35" is a 60mm-long, 35mm-wide bike base), while our
        // RectangleBase runs its local +Z (HeightInches) along the facing and +X (WidthInches) across the
        // frontage. So length maps to Height, width to Width — mapping them positionally faced every
        // rectangular model along its short axis, inflating frontage for LoS/overlap/coherency.
        internal static BaseFileEntry MapBase(OprBases? bases, IEnumerable<SpecialRuleEntry>? rules = null,
            string? unitName = null, Action<string>? warn = null)
        {
            BaseFileEntry? declared = TryParseBase(bases?.Round, preferCircle: true)
                ?? TryParseBase(bases?.Square, preferCircle: false);
            if (declared != null) return declared;

            // #225 defect B: OPR emits "none" for vehicles/superheavies, so there is nothing to import.
            // Estimating beats the old silent 28mm-circle fallback, which put every titan and tank on an
            // infantry dot — but it IS an estimate, so say so rather than letting it pass unnoticed.
            BaseFileEntry estimated = DefaultBaseEstimator.Estimate(
                rules ?? Enumerable.Empty<SpecialRuleEntry>(), out string describe);
            warn?.Invoke($"no base declared for '{unitName ?? "unit"}' - estimated {describe}");
            return estimated;
        }

        private static BaseFileEntry? TryParseBase(string? spec, bool preferCircle)
        {
            spec = spec?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(spec) || spec == "none") return null;

            int x = spec.IndexOf('x');
            if (x < 0)
            {
                if (!float.TryParse(spec, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float mm) || mm <= 0) return null;
                return preferCircle
                    ? new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = mm / MmPerInch }
                    : new BaseFileEntry { Shape = EBaseShapeKind.Rectangle, WidthInches = mm / MmPerInch, HeightInches = mm / MmPerInch };
            }

            // Length first, width second (see #225 note on MapBase): length is the facing axis → Height.
            if (!float.TryParse(spec[..x], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float lengthMm) || lengthMm <= 0) return null;
            if (!float.TryParse(spec[(x + 1)..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float widthMm) || widthMm <= 0) return null;
            return new BaseFileEntry { Shape = EBaseShapeKind.Rectangle, WidthInches = widthMm / MmPerInch, HeightInches = lengthMm / MmPerInch };
        }

        /// <summary>Maps an OPR item (unit-level or gain) to an <see cref="ItemEntry"/>: name + its rule content.
        /// Non-rule content (nested weapons) is the caller's job to route.</summary>
        private static ItemEntry MapItem(OprGain g) => new()
        {
            Name = g.Name ?? "Item",
            Quantity = Math.Max(1, g.Count ?? 1),
            Rules = (g.Content ?? new())
                .Where(c => c.Type == "ArmyBookRule")
                .Select(c => MapRule(new OprRule { Name = c.Name, Rating = c.Rating }))
                .ToList(),
        };

        // ── OPR JSON DTOs (only the fields we consume; lore/image/meta fields are ignored) ──────────────────

        private sealed class OprBook
        {
            public string? Name { get; set; }
            public string? VersionString { get; set; }
            public List<OprUnit>? Units { get; set; }
            public List<OprPackage>? UpgradePackages { get; set; }
            public List<OprSpell>? Spells { get; set; }
        }

        private sealed class OprSpell
        {
            public string? Name { get; set; }
            public int? Threshold { get; set; }
            public string? Effect { get; set; }
        }

        private sealed class OprUnit
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public int Size { get; set; }
            public int Cost { get; set; }
            public int Quality { get; set; }
            public int Defense { get; set; }
            public List<OprWeapon>? Weapons { get; set; }
            public List<OprRule>? Rules { get; set; }
            public List<OprGain>? Items { get; set; }
            public List<string>? Upgrades { get; set; }
            public OprBases? Bases { get; set; }
        }

        internal sealed class OprBases
        {
            public string? Round { get; set; }
            public string? Square { get; set; }
        }

        private sealed class OprWeapon
        {
            public string? Name { get; set; }
            public int? Count { get; set; }
            public int? Range { get; set; }
            public int? Attacks { get; set; }
            public List<OprRule>? SpecialRules { get; set; }
        }

        private sealed class OprRule
        {
            public string? Name { get; set; }
            public int? Rating { get; set; }
        }

        private sealed class OprPackage
        {
            public string? Uid { get; set; }
            public List<OprSection>? Sections { get; set; }
        }

        private sealed class OprSection
        {
            public string? Id { get; set; }
            public string? Uid { get; set; }
            public string? Label { get; set; }
            public string? Variant { get; set; }
            public OprAffects? Affects { get; set; }
            public OprAffects? Select { get; set; } // same {type,value} shape as affects
            public List<string>? Targets { get; set; }
            public List<OprOption>? Options { get; set; }
        }

        private sealed class OprAffects
        {
            public string? Type { get; set; }
            public int? Value { get; set; }
        }

        private sealed class OprOption
        {
            public string? Id { get; set; }
            public string? Uid { get; set; }
            public string? Label { get; set; }
            // Nullable on purpose (#219): OPR omits `cost` entirely on options it prices in its own points
            // algorithm rather than publishing a static number, and writes an explicit 0 for genuinely free
            // ones. A non-nullable int collapsed both onto 0 and made unpriced upgrades look free.
            public int? Cost { get; set; }
            // #219: when the flat `cost` scalar above is absent, OPR still publishes the price here, keyed by
            // the referencing unit's id - the SAME option can cost differently on different units (a pistol
            // swap that is 10 pts on one hero, 5 on another), which is exactly why this is a per-unit array
            // and not one number. Recovers what looked "unpriced" when only `cost` was read.
            public List<OprOptionCost>? Costs { get; set; }
            public List<OprGain>? Gains { get; set; }
        }

        private sealed class OprOptionCost
        {
            public int? Cost { get; set; }
            public string? UnitId { get; set; }
        }

        private sealed class OprGain
        {
            public string? Type { get; set; }
            public string? Name { get; set; }
            public int? Count { get; set; }
            public int? Range { get; set; }
            public int? Attacks { get; set; }
            public int? Rating { get; set; }
            public List<OprRule>? SpecialRules { get; set; }
            public List<OprGain>? Content { get; set; }
        }
    }
}
