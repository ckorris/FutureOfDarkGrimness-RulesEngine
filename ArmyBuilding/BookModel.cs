using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #153 (P0) — the catalog ("book") an Army-Forge-style builder picks from. App-side only; the
    // compiler (ListCompiler) turns a BookFile + a BuilderList of selections into the engine's existing
    // ArmyListFile / .fdgarmy, so nothing here is known to the engine. Reuses the engine's authored-army
    // vocabulary (WeaponFileEntry / SpecialRuleEntry / BaseFileEntry / SpecialRuleDefinition / SpellDefinition)
    // so a compiled unit is byte-for-byte the same shape as a hand-authored one, and everything rides the
    // same System.Text.Json kind-schema (RuleJson.Options).

    /// <summary>
    /// A faction "book": a fixed roster of pre-statted units plus the rule/spell definitions they reference.
    /// A full snapshot of this is embedded inside every army the builder saves (see <c>BuiltArmyFile</c>), so
    /// an old army stays reproducible and re-editable even after the catalog book changes in a later version.
    /// </summary>
    public class BookFile
    {
        public const string EXTENSION_NO_PERIOD = "fdgbook";
        public const string EXTENSION_WITH_PERIOD = "." + EXTENSION_NO_PERIOD;

        public string Name { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;

        /// <summary>Author-set version string (e.g. "1.0.0"). Frozen into a saved army for provenance.</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Provenance for imported books — who the game-data came from (e.g. an OnePageRules
        /// Army Forge snapshot). Empty for hand-authored books.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>License the imported data is used under (e.g. CC-BY-SA 4.0). Empty for hand-authored books.</summary>
        public string License { get; set; } = string.Empty;

        /// <summary>
        /// #239: the faction's default effect-set keys — copied onto every army compiled from this
        /// book (<see cref="ArmyListFile.DefaultRangedEffectSet"/>), the fallback for weapons no
        /// keyword claims. Opaque presentation identifiers; null falls through to the front-end's
        /// global default. Stamped by <see cref="WeaponEffectAssigner"/> at import / retrofit.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? DefaultRangedEffectSet { get; set; }

        /// <inheritdoc cref="DefaultRangedEffectSet"/>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? DefaultMeleeEffectSet { get; set; }

        public List<RosterUnit> Units { get; set; } = new();

        /// <summary>Rule definitions the roster's units/upgrades reference. Copied onto a compiled army so
        /// the engine's <c>RuleValidator</c> gate passes and rule references resolve (audit finding 3).</summary>
        public List<SpecialRuleDefinition> RuleDefinitions { get; set; } = new();

        /// <summary>The army-wide spell list (#033): available to any unit carrying Caster(X). Copied onto a
        /// compiled army as-is.</summary>
        public List<SpellDefinition> Spells { get; set; } = new();
    }

    /// <summary>
    /// A pre-statted roster entry. Base stats are fixed; customisation happens through <see cref="Sections"/>.
    /// Weapons are unit-level with per-type <c>Quantity</c> exactly as <c>UnitFileEntry</c> expects — the engine
    /// expands + round-robins them across models at runtime, so "one model's weapon" is expressed as an
    /// aggregate count, not an explicit per-model slot (audit finding 1).
    /// </summary>
    public class RosterUnit
    {
        /// <summary>Stable within-book id, referenced by a <c>BuilderUnit</c> and by upgrade targeting.</summary>
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public int Quality { get; set; }
        public int Defense { get; set; }

        /// <summary>Model count / cost at the unit's default size; <see cref="MinModels"/>..<see cref="MaxModels"/>
        /// bound the allowed range (equal = fixed-size). Size changes are otherwise modelled as AddModels sections.</summary>
        public int BaseModelCount { get; set; } = 1;
        public int MinModels { get; set; } = 1;
        public int MaxModels { get; set; } = 1;

        /// <summary>Cost of the unit at <see cref="BaseModelCount"/> with no upgrades selected.</summary>
        public int BasePointCost { get; set; }

        public BaseFileEntry Base { get; set; } = new();

        public List<WeaponFileEntry> Weapons { get; set; } = new();
        public List<SpecialRuleEntry> Rules { get; set; } = new();

        /// <summary>Named wargear the unit carries (e.g. "Combat Shields") — each a bundle of granted rules.
        /// Items keep their names so Replace sections can target them ("Replace all ... and Combat Shields");
        /// at compile time their rules flatten into the unit's SpecialRules.</summary>
        public List<ItemEntry> Items { get; set; } = new();

        public List<UpgradeSection> Sections { get; set; } = new();
    }

    /// <summary>A named piece of wargear: carries rules, no attack profile. Kept distinct from weapons so it
    /// can be a Replace target and display under its own name.</summary>
    public class ItemEntry
    {
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public List<SpecialRuleEntry> Rules { get; set; } = new();
    }

    /// <summary>What an upgrade section does to the unit when an option is chosen.</summary>
    public enum UpgradeVariant
    {
        /// <summary>Swap the weapon/rule named in <see cref="UpgradeSection.Targets"/> for the option's gains.</summary>
        Replace,
        /// <summary>Add the option's gains (wargear / rules) on top of the current loadout.</summary>
        Upgrade,
        /// <summary>Pick between <see cref="UpgradeSection.MinPicks"/> and <see cref="UpgradeSection.MaxPicks"/> options.</summary>
        PickN,
        /// <summary>Add models to the unit (each option's <see cref="UpgradeOption.ModelsGained"/> &gt; 0).</summary>
        AddModels,
    }

    /// <summary>How many of the unit's models an option touches — drives cost scaling (audit finding 5).</summary>
    public enum UpgradeAffects
    {
        /// <summary>Exactly one model. Cost = option cost.</summary>
        One,
        /// <summary>0..N models, chosen by a count stepper. Cost = count × option cost — Army Forge emits
        /// one selectedUpgrades entry per application, so each pick is charged (unverified arithmetically:
        /// no priced "any" option has turned up in a real share list yet — #218).</summary>
        Any,
        /// <summary>Every eligible model. Cost is FLAT — charged once for the unit, NOT scaled by the models
        /// it touches (#218, verified 2026-07-19 against a real share list). The effect still applies to
        /// every eligible model; only the price is levied once.</summary>
        All,
    }

    /// <summary>A group of mutually-related options presented together (e.g. "Replace any Heavy Razor Claw").</summary>
    public class UpgradeSection
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        public UpgradeVariant Variant { get; set; } = UpgradeVariant.Upgrade;
        public UpgradeAffects Affects { get; set; } = UpgradeAffects.One;

        /// <summary>Weapon/rule names removed when an option is applied (Replace only).</summary>
        public List<string> Targets { get; set; } = new();

        /// <summary>For a counted (<see cref="UpgradeAffects.Any"/>) section, the max number of applications
        /// (0 = unbounded, limited only by how many targets are present). Captures OPR "up to N" / "exactly N".
        /// Ignored for One (always 1) and All (all matched targets).</summary>
        public int MaxApplications { get; set; }

        /// <summary>Pick bounds for <see cref="UpgradeVariant.PickN"/> (ignored otherwise).</summary>
        public int MinPicks { get; set; } = 0;
        public int MaxPicks { get; set; } = 1;

        /// <summary>#383 — each application is one MODEL's pick (OPR's "Any model may replace/take ..."
        /// sections: `select: any` + `model: true` on a replace/attachment section), so the section's TOTAL
        /// applications across ALL its options are capped at the unit's CURRENT model count, on top of each
        /// option's own bounds. Always paired with <see cref="UpgradeAffects.Any"/> (a counted stepper,
        /// charged per application); false everywhere else, including the same-shaped "Upgrade with any"
        /// subset sections, whose options are each takeable once. Serialized only when true, so the 22
        /// stamped corpus sections carry it and every other section stays byte-identical.</summary>
        [System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool PerModelBudget { get; set; }

        public List<UpgradeOption> Options { get; set; } = new();

        /// <summary>Counted sections use a numeric quantity (AddModels, or an "any"-model effect); the rest are
        /// on/off toggles. Derived — not serialized.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsCounted => Variant == UpgradeVariant.AddModels || Affects == UpgradeAffects.Any;
    }

    /// <summary>One selectable choice within a section. <see cref="Cost"/> is per application (see <see cref="UpgradeAffects"/>).</summary>
    public class UpgradeOption
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        /// <summary>Points added per application. 0 when <see cref="CostUnpriced"/> — see that flag before
        /// treating a 0 here as "free".</summary>
        public int Cost { get; set; }

        /// <summary>OPR published no price for this option (#219): the source book omits the `cost` key
        /// entirely, because Army Forge derives the number in its own points algorithm at list-build time
        /// rather than storing it. Distinct from a genuine free option, which OPR writes as an explicit 0.
        /// We cannot recover the real price from any OPR endpoint, so <see cref="Cost"/> stays 0 and
        /// consumers disclose the gap instead of asserting a total they can't stand behind.</summary>
        public bool CostUnpriced { get; set; }

        public List<WeaponFileEntry> WeaponsGained { get; set; } = new();
        public List<SpecialRuleEntry> RulesGained { get; set; } = new();

        /// <summary>Named wargear gained (e.g. "Sgt. Armor"), keeping the item's name for display and for
        /// later Replace targeting; its rules flatten into the unit at compile time.</summary>
        public List<ItemEntry> ItemsGained { get; set; } = new();

        /// <summary>Models added to the unit (AddModels sections). 0 for weapon/rule upgrades.</summary>
        public int ModelsGained { get; set; }
    }
}
