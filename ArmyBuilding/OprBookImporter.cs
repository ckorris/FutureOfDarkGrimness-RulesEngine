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

        public static BookFile Import(string oprJson, string source, string license, Action<string>? warn = null)
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
                Description: $"This unit counts as being in {kind} Terrain on its next move — {consequence} (from {spellName}).");
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
