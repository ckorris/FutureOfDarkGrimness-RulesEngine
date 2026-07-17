using System;
using System.Collections.Generic;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #239 — the AUTHORING-time policy for weapon effect-set keys: name keywords for cross-faction
    // tech ("plasma", "fusion"...), per-faction defaults, and rare (faction, name) overrides. It
    // bakes explicit keys into data (books at import, armies at compile / retrofit); the runtime
    // engine only ever transports the resulting opaque strings (Weapon.EffectKey -> AttackBeat),
    // and the front-end maps them to visuals/sounds. Deliberately NOT consulted at render time:
    // keys ride the data so they survive renames/localization and hand-authored content.
    public static class WeaponEffectAssigner
    {
        /// <summary>The effect-set vocabulary (#239). The strings are the data-file keys; front-ends
        /// define what each looks/sounds like and treat unknown keys as their global default.</summary>
        public static class Sets
        {
            // Ranged.
            public const string PlasmaBolt      = "plasma-bolt";
            public const string FusionMelta     = "fusion-melta";
            public const string FlameJet        = "flame-jet";
            public const string GravityPulse    = "gravity-pulse";
            public const string GaussParticle   = "gauss-particle";
            public const string LaserBeam       = "laser-beam";
            public const string MissileRocket   = "missile-rocket";
            public const string MortarArtillery = "mortar-artillery";
            public const string BioOrganic      = "bio-organic";
            public const string StormTracer     = "storm-tracer";
            public const string BallisticSlug   = "ballistic-slug";   // global ranged default
            public const string ArcanePsychic   = "arcane-psychic";
            public const string ShardCrystal    = "shard-crystal";    // High Elf Fleets bespoke

            // Melee.
            public const string EnergyBlade       = "energy-blade";
            public const string TitanImpact       = "titan-impact";
            public const string ShockMelee        = "shock-melee";
            public const string ChainBlade        = "chain-blade";
            public const string ToxicMelee        = "toxic-melee";
            public const string DaemonArcaneMelee = "daemon-arcane-melee";
            public const string SpearPierce       = "spear-pierce";
            public const string ClawRend          = "claw-rend";
            public const string CrudeMelee        = "crude-melee";
            public const string BladeStandard     = "blade-standard"; // global melee default
        }

        // Cross-faction tech keywords, priority-ordered — first matching set wins (case-insensitive
        // substring on the weapon name). Deliberately ONLY distinctive tech words: generic gun words
        // (rifle, pistol, machinegun) stay unmatched so they fall through to the faction default.
        private static readonly (string Set, string[] Keywords)[] RangedKeywords =
        {
            (Sets.PlasmaBolt,      new[] { "plasma" }),
            (Sets.FusionMelta,     new[] { "fusion", "melta", "fuser" }),
            (Sets.FlameJet,        new[] { "flame" }),
            (Sets.GravityPulse,    new[] { "gravity" }),
            (Sets.MissileRocket,   new[] { "missile", "rocket", "rpg" }),
            (Sets.MortarArtillery, new[] { "mortar", "artillery", "grenade", "bomb", "siege", "demolition", "frag" }),
            (Sets.BioOrganic,      new[] { "bio", "spit", "spore", "acid", "venom", "toxin", "toxic", "vomit", "miasma" }),
            (Sets.GaussParticle,   new[] { "gauss", "flux", "atom", "shock", "reaper" }),
            (Sets.LaserBeam,       new[] { "laser", "beam", "photon", "pulse", "monolith" }),
            // Before storm/ballistic so "Magic Bolt"-style names read as arcane, not tracer fire.
            (Sets.ArcanePsychic,   new[] { "magic", "psychic", "hex", "curse", "ritual", "chakram", "fireball" }),
            (Sets.StormTracer,     new[] { "storm", "bolt" }),
            (Sets.BallisticSlug,   new[] { "bullet", "slug", "buckshot", "revolver" }),
        };

        private static readonly (string Set, string[] Keywords)[] MeleeKeywords =
        {
            (Sets.EnergyBlade,       new[] { "energy", "plasma", "relic", "hyper" }),
            (Sets.TitanImpact,       new[] { "titan", "walker", "stomp", "hull", "crushing" }),
            (Sets.ShockMelee,        new[] { "shock", "electro", "taser", "stun" }),
            (Sets.ChainBlade,        new[] { "chain", "saw", "buzz" }),
            (Sets.ToxicMelee,        new[] { "venom", "toxin", "toxic", "plague", "acid", "infected", "poison", "putrid", "fungal", "miasma" }),
            // "power" belongs to the Wormhole Daemons' Power Staff/Spear/Claw family — the rare
            // non-daemonic "Power X" gets a (faction, name) override below instead of a keyword change.
            (Sets.DaemonArcaneMelee, new[] { "cursed", "hexed", "power", "ritual", "perfect", "exalted", "daemon" }),
            (Sets.SpearPierce,       new[] { "spear", "lance", "pike", "halberd", "glaive", "scythe" }),
            (Sets.ClawRend,          new[] { "claw", "fang", "bite", "jaw", "talon", "razor", "whip", "slash", "serrated", "rend", "swarm" }),
            (Sets.CrudeMelee,        new[] { "fist", "club", "mace", "flail", "hammer", "bash", "axe", "knuckle", "gauntlet", "crew", "pick", "maul" }),
            (Sets.BladeStandard,     new[] { "sword", "blade", "dagger", "knife" }),
        };

        // Exact (faction, weapon name) overrides for the handful of names the keywords misread in a
        // specific book's context. Checked before the keyword tables.
        private static readonly Dictionary<(string Faction, string Name), string> NameOverrides = new()
        {
            // A lizard's gauntlet, not a daemon relic ("power" would route it to daemon-arcane-melee).
            [("Saurian Starhost", "Power Claw")] = Sets.ClawRend,
        };

        // Per-faction default sets (ranged, melee) — the fallback look for a weapon no keyword
        // claims, chosen per book (#239 survey, 2026-07-16). Keys are the books' Faction strings.
        private static readonly Dictionary<string, (string Ranged, string Melee)> FactionDefaultsTable = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Alien Hives"]                  = (Sets.BioOrganic, Sets.ClawRend),
            ["Battle Brothers"]              = (Sets.StormTracer, Sets.EnergyBlade),
            ["Blessed Sisters"]              = (Sets.StormTracer, Sets.EnergyBlade),
            ["Blood Brothers"]               = (Sets.StormTracer, Sets.EnergyBlade),
            ["Blood Prime Brothers"]         = (Sets.StormTracer, Sets.EnergyBlade),
            ["Change Disciples"]             = (Sets.BallisticSlug, Sets.EnergyBlade),
            ["Custodian Brothers"]           = (Sets.StormTracer, Sets.SpearPierce),
            ["DAO Union"]                    = (Sets.LaserBeam, Sets.EnergyBlade),
            ["Dark Brothers"]                = (Sets.StormTracer, Sets.EnergyBlade),
            ["Dark Elf Raiders"]             = (Sets.BioOrganic, Sets.ClawRend),
            ["Dark Prime Brothers"]          = (Sets.StormTracer, Sets.EnergyBlade),
            ["Dwarf Guilds"]                 = (Sets.BallisticSlug, Sets.ShockMelee),
            ["Elven Jesters"]                = (Sets.MortarArtillery, Sets.BladeStandard),
            ["Eternal Dynasty"]              = (Sets.LaserBeam, Sets.TitanImpact),
            ["Goblin Reclaimers"]            = (Sets.BallisticSlug, Sets.CrudeMelee),
            ["Havoc Brothers"]               = (Sets.BallisticSlug, Sets.EnergyBlade),
            ["High Elf Fleets"]              = (Sets.ShardCrystal, Sets.EnergyBlade),
            ["Human Defense Force"]          = (Sets.BallisticSlug, Sets.CrudeMelee),
            ["Human Inquisition"]            = (Sets.BallisticSlug, Sets.EnergyBlade),
            ["Infected Colonies"]            = (Sets.BioOrganic, Sets.ToxicMelee),
            ["Jackals"]                      = (Sets.BallisticSlug, Sets.SpearPierce),
            ["Knight Brothers"]              = (Sets.StormTracer, Sets.EnergyBlade),
            ["Knight Prime Brothers"]        = (Sets.StormTracer, Sets.EnergyBlade),
            ["Lust Disciples"]               = (Sets.BallisticSlug, Sets.EnergyBlade),
            ["Machine Cults"]                = (Sets.BallisticSlug, Sets.ShockMelee),
            ["Orc Marauders"]                = (Sets.BallisticSlug, Sets.CrudeMelee),
            ["Plague Disciples"]             = (Sets.BioOrganic, Sets.ToxicMelee),
            ["Prime Brothers"]               = (Sets.StormTracer, Sets.EnergyBlade),
            ["Ratmen Clans"]                 = (Sets.BallisticSlug, Sets.ShockMelee),
            ["Rebel Guerrillas"]             = (Sets.BallisticSlug, Sets.EnergyBlade),
            ["Robot Legions"]                = (Sets.GaussParticle, Sets.TitanImpact),
            ["Saurian Starhost"]             = (Sets.GaussParticle, Sets.ClawRend),
            ["Soul-Snatcher Cults"]          = (Sets.BallisticSlug, Sets.CrudeMelee),
            ["Titan Lords"]                  = (Sets.FusionMelta, Sets.TitanImpact),
            ["Titan Lords Change Disciples"] = (Sets.FusionMelta, Sets.TitanImpact),
            ["Titan Lords Lust Disciples"]   = (Sets.FusionMelta, Sets.TitanImpact),
            ["Titan Lords Plague Disciples"] = (Sets.FusionMelta, Sets.TitanImpact),
            ["Titan Lords War Disciples"]    = (Sets.FusionMelta, Sets.TitanImpact),
            ["War Disciples"]                = (Sets.BallisticSlug, Sets.EnergyBlade),
            ["Watch Brothers"]               = (Sets.StormTracer, Sets.EnergyBlade),
            ["Watch Prime Brothers"]         = (Sets.StormTracer, Sets.EnergyBlade),
            ["Wolf Brothers"]                = (Sets.StormTracer, Sets.EnergyBlade),
            ["Wolf Prime Brothers"]          = (Sets.StormTracer, Sets.EnergyBlade),
            ["Wormhole Daemons of Change"]   = (Sets.ArcanePsychic, Sets.DaemonArcaneMelee),
            ["Wormhole Daemons of Lust"]     = (Sets.BallisticSlug, Sets.DaemonArcaneMelee),
            ["Wormhole Daemons of Plague"]   = (Sets.BioOrganic, Sets.ToxicMelee),
            ["Wormhole Daemons of War"]      = (Sets.BioOrganic, Sets.BladeStandard),
        };

        /// <summary>The faction's default (ranged, melee) sets, or (null, null) for a faction the
        /// table doesn't know (hand-authored books/armies) — the front-end global default applies.</summary>
        public static (string? Ranged, string? Melee) FactionDefaults(string faction) =>
            FactionDefaultsTable.TryGetValue(faction ?? string.Empty, out var d) ? (d.Ranged, d.Melee) : (null, null);

        /// <summary>
        /// The keyword/override match for a weapon name, or null when nothing distinctive matches
        /// (the army default covers it at load). <paramref name="faction"/> feeds the rare exact
        /// (faction, name) overrides; pass what the data has — an unknown faction just skips them.
        /// </summary>
        public static string? Match(string faction, WeaponFileEntry weapon) =>
            Match(faction, weapon.Name, isRanged: weapon.RangeInches > 0);

        /// <inheritdoc cref="Match(string, WeaponFileEntry)"/>
        public static string? Match(string faction, string weaponName, bool isRanged)
        {
            if (string.IsNullOrWhiteSpace(weaponName)) return null;

            if (NameOverrides.TryGetValue((faction ?? string.Empty, weaponName.Trim()), out string? overridden))
                return overridden;

            foreach ((string set, string[] keywords) in isRanged ? RangedKeywords : MeleeKeywords)
                foreach (string keyword in keywords)
                    if (weaponName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        return set;

            return null;
        }

        /// <summary>Stamps the faction's default sets onto a book (only where unset). Returns whether
        /// anything changed. Per-weapon keys are NOT written into books — they bake at compile time,
        /// so a keyword-table improvement reaches every future army without re-patching 47 books.</summary>
        public static bool ApplyToBook(BookFile book)
        {
            (string? ranged, string? melee) = FactionDefaults(book.Faction);
            bool changed = false;
            if (book.DefaultRangedEffectSet == null && ranged != null) { book.DefaultRangedEffectSet = ranged; changed = true; }
            if (book.DefaultMeleeEffectSet == null && melee != null) { book.DefaultMeleeEffectSet = melee; changed = true; }
            return changed;
        }

        /// <summary>
        /// One-shot retrofit for an existing .fdgarmy (#239): fills the army-level defaults from the
        /// faction table and bakes keyword keys onto weapons that have none. Explicit keys already in
        /// the file are never touched. A <see cref="BuiltArmyFile"/>'s embedded book gets its defaults
        /// stamped too, so a later Forge re-save keeps them. Returns whether anything changed.
        /// </summary>
        public static bool ApplyToArmy(ArmyListFile army)
        {
            bool changed = false;

            (string? ranged, string? melee) = FactionDefaults(army.Faction);
            if (army.DefaultRangedEffectSet == null && ranged != null) { army.DefaultRangedEffectSet = ranged; changed = true; }
            if (army.DefaultMeleeEffectSet == null && melee != null) { army.DefaultMeleeEffectSet = melee; changed = true; }

            foreach (UnitFileEntry unit in army.Units)
            {
                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    if (weapon.EffectSet != null) continue;
                    string? match = Match(army.Faction, weapon);
                    if (match != null) { weapon.EffectSet = match; changed = true; }
                }
            }

            if (army is BuiltArmyFile built && built.Book != null)
                changed |= ApplyToBook(built.Book);

            return changed;
        }
    }
}
