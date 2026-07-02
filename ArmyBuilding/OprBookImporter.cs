using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #153 (P0b) — one-time importer: OnePageRules Army Forge JSON (/api/army-books/<uid>) → our BookFile.
    // Captures FUNCTIONAL game data only (unit stats, weapons, point costs, upgrade options, rule names). It
    // never reads the book's background/lore prose. Imported data is OPR's, used under CC-BY-SA — Import stamps
    // Source + License on the book. Special rules import as name references; the engine already skips rules it
    // doesn't implement (so unimplemented OPR rules are inert, not errors, and no RuleDefinitions are emitted →
    // the RuleValidator gate is trivially satisfied).
    //
    // Fidelity notes (v1 — recorded, not silently dropped):
    //   • affects "exactly N" / "up to N" → mapped to Any (user picks the count); the exact/max bound is not
    //     yet enforced (P4 validation).
    //   • Model-count upgrades (combined units / "+N models") are not synthesized as AddModels — units import
    //     at their base size.
    //   • Caster spell lists (book.spells) are not imported (our spells need engine Effect graphs).
    public static class OprBookImporter
    {
        // OPR's JSON types numeric fields loosely — a rating/count can arrive as a number OR a string (and a
        // string field like a base size could plausibly arrive as a bare number). Tolerant converters keep the
        // import from throwing on that.
        private static readonly JsonSerializerOptions ReadOpts = new()
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

        public static BookFile Import(string oprJson, string source, string license)
        {
            OprBook opr = JsonSerializer.Deserialize<OprBook>(oprJson, ReadOpts)
                ?? throw new InvalidOperationException("OPR army-book JSON did not deserialize.");

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

            foreach (OprUnit unit in opr.Units ?? new())
                book.Units.Add(MapUnit(unit, packages));

            return book;
        }

        private static RosterUnit MapUnit(OprUnit unit, IReadOnlyDictionary<string, OprPackage> packages)
        {
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
                Base = MapBase(unit.Bases),
                Weapons = (unit.Weapons ?? new()).Select(MapWeapon).ToList(),
                Rules = (unit.Rules ?? new()).Select(MapRule).ToList(),
                Items = (unit.Items ?? new()).Select(MapItem).ToList(),
            };

            // Resolve the unit's referenced upgrade packages → their sections.
            foreach (string pkgUid in unit.Upgrades ?? new())
                if (packages.TryGetValue(pkgUid, out OprPackage? pkg))
                    foreach (OprSection section in pkg.Sections ?? new())
                        roster.Sections.Add(MapSection(section));

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

        private static UpgradeSection MapSection(OprSection s)
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

            return new UpgradeSection
            {
                Id = s.Id ?? s.Uid ?? Guid.NewGuid().ToString("N")[..8],
                Label = s.Label ?? string.Empty,
                Variant = s.Variant == "replace" ? UpgradeVariant.Replace : UpgradeVariant.Upgrade,
                Affects = affects,
                MaxApplications = maxApplications,
                Targets = s.Targets ?? new(),
                Options = (s.Options ?? new()).Select(MapOption).ToList(),
            };
        }

        private static UpgradeOption MapOption(OprOption o)
        {
            var option = new UpgradeOption
            {
                Id = o.Id ?? o.Uid ?? Guid.NewGuid().ToString("N")[..8],
                Label = o.Label ?? string.Empty,
                Cost = o.Cost,
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

        private const float MmPerInch = 25.4f;

        // OPR bases come as {round, square} in millimetres — "25", "120x92" (an oval), "none", or "". Prefer
        // the round basing (GDF standard): a single number → Circle; "WxH" → our Rectangle footprint (the
        // engine has no oval; #149's rectangle is the dimension-faithful stand-in). Fall back to the square
        // spec, else keep the default 28mm circle.
        private static BaseFileEntry MapBase(OprBases? bases) =>
            TryParseBase(bases?.Round, preferCircle: true)
            ?? TryParseBase(bases?.Square, preferCircle: false)
            ?? new BaseFileEntry();

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

            if (!float.TryParse(spec[..x], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float w) || w <= 0) return null;
            if (!float.TryParse(spec[(x + 1)..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float h) || h <= 0) return null;
            return new BaseFileEntry { Shape = EBaseShapeKind.Rectangle, WidthInches = w / MmPerInch, HeightInches = h / MmPerInch };
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

        private sealed class OprBases
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
            public int Cost { get; set; }
            public List<OprGain>? Gains { get; set; }
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
