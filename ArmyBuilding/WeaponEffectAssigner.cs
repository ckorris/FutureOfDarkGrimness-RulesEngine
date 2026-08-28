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

            // Ranged, Age of Fantasy (#378 mints the keys; #379 implements visuals/sounds - until
            // then front-ends draw them as their global default, by design).
            public const string ArrowLoose   = "arrow-loose";
            public const string CrossbowBolt = "crossbow-bolt";
            public const string SlingStone   = "sling-stone";
            public const string ThrownSpear  = "thrown-spear";
            public const string BallistaBolt = "ballista-bolt";
            public const string BreathFlame  = "breath-flame";
            public const string ArcaneBolt   = "arcane-bolt";

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

            // Melee, Age of Fantasy (#378/#379, as above).
            public const string GreatWeaponSmash = "great-weapon-smash";
            public const string SpectralTouch    = "spectral-touch";
            public const string BeastMaw         = "beast-maw";

            // Melee, cross-system (#379): minted with the AoF pass but applied to BOTH vocabularies
            // via the form upgrades / rule override below (GDF has Toxin Claws and strafing bomb
            // racks too).
            public const string ToxicRend  = "toxic-rend";   // toxic payload, claw motion
            public const string BombingRun = "bombing-run";  // range-0 Strafing aerial bomb drops
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

        // Age of Fantasy keyword tables (#378) - a separate vocabulary, not additions to the GDF rows:
        // AoF weapon names collide with GDF tech words in the wrong direction ("Fire Bolt Thrower" must
        // not read as storm-tracer fire, "Chain-Sword" is not a chainsaw). Same discipline as the GDF
        // tables: only distinctive words; generics (hand weapon, crew, CCW) fall to the faction default.
        // Priority-ordered - crossbows/ballistae before "bow", bio/flame before the gunpowder row so
        // "Toxin Bombs" and "Flame Pistol" read by their payload, not their casing.
        private static readonly (string Set, string[] Keywords)[] AofRangedKeywords =
        {
            (Sets.BallistaBolt,    new[] { "ballista", "bolt thrower" }),
            (Sets.CrossbowBolt,    new[] { "crossbow", "handbow" }),
            (Sets.ArrowLoose,      new[] { "bow" }),
            (Sets.ThrownSpear,     new[] { "javelin", "throwing", "harpoon" }),
            (Sets.BioOrganic,      new[] { "venom", "toxin", "toxic", "spit", "acid", "spore", "plague", "blowpipe", "dart" }),
            (Sets.BreathFlame,     new[] { "flame", "breath", "lava", "spout", "blaze", "scorcher" }),
            // "stare"/"gaze"/"shriek"/"screech": psychic attacks with no magic word in the name
            // (Death Stare, Mind-Piercer Screech) must not fall to a faction's crossbow/javelin
            // default (#379 audit).
            (Sets.ArcaneBolt,      new[] { "magic", "arcane", "hex", "curse", "fireball", "staff", "wand", "banish", "summon", "stare", "gaze", "shriek", "screech" }),
            (Sets.SlingStone,      new[] { "sling", "throw rock", "throw stone", "hurl", "stone thrower", "catapult" }),
            (Sets.MortarArtillery, new[] { "bomb", "grenade", "firework", "rocket", "mortar", "siege" }),
            (Sets.BallisticSlug,   new[] { "rifle", "pistol", "gun", "cannon", "blunderbuss", "gatling", "firepowder", "musket" }),
        };

        private static readonly (string Set, string[] Keywords)[] AofMeleeKeywords =
        {
            (Sets.SpectralTouch,    new[] { "spectral", "soul", "spirit", "ghost", "mourning" }),
            (Sets.ToxicMelee,       new[] { "venom", "toxin", "toxic", "plague", "acid", "poison", "infected", "stinger", "censer" }),
            (Sets.DaemonArcaneMelee, new[] { "power", "ritual", "hexed", "cursed", "daemon", "exalted", "perfect", "mutated", "pain" }),
            (Sets.EnergyBlade,      new[] { "flame", "magma", "fire", "holy", "blessed", "runic", "celestial", "magic" }),
            (Sets.GreatWeaponSmash, new[] { "great weapon", "greatsword", "great hammer", "great axe", "great mace", "great glaive" }),
            (Sets.BeastMaw,         new[] { "jaws", "jaw", "bite", "fang", "maw", "tusk", "tentacle", "pincer", "beak", "horn" }),
            (Sets.SpearPierce,      new[] { "spear", "lance", "pike", "halberd", "glaive", "scythe", "trident" }),
            (Sets.ClawRend,         new[] { "claw", "talon", "rend", "swarm", "razor", "whip", "slash", "serrated" }),
            // "hoof"/"hooves": 41 refs across 13 factions were kicking as sword-slashes (#379 audit);
            // a mount's trample is a blunt impact.
            (Sets.TitanImpact,      new[] { "stomp", "hull", "crushing", "hooves", "hoof" }),
            (Sets.CrudeMelee,       new[] { "fist", "club", "mace", "flail", "hammer", "axe", "pick", "maul", "drill", "chain", "bash" }),
            (Sets.BladeStandard,    new[] { "sword", "blade", "dagger", "knife", "falchion" }),
        };

        // #379 melee form upgrades, applied AFTER the keyword tables so payload rows keep their
        // priority ("Great Plague Hammer" stays toxic-melee, "Heavy Energy Hammer" stays
        // energy-blade). Cross-system: the word lists come from both corpora.
        //
        // A crude-melee name that pairs a size word with a blunt noun ("Giant Hammer", "Great Bone
        // Mace", "Heavy Flail") is a two-handed weapon: it swings overhead (great-weapon-smash)
        // instead of slashing. A bare "Mace"/"Club"/"Flail" stays crude-melee.
        private static readonly string[] BluntSizeWords = { "great", "giant", "mega", "ultra", "heavy", "titan", "thunder", "meteor", "massive" };
        private static readonly string[] BluntNouns = { "hammer", "mace", "club", "flail", "maul" };

        // A toxic-melee name that is also a claw/jaw ("Toxin Claws", "Toxic Maw") keeps the claw's
        // rake motion with the toxic accent instead of slashing like a poisoned sword.
        private static readonly string[] ClawNouns = { "claw", "talon", "jaw", "maw", "bite", "fang" };

        private static string UpgradeMeleeForm(string set, string weaponName)
        {
            if (set == Sets.CrudeMelee && ContainsAny(weaponName, BluntSizeWords) && ContainsAny(weaponName, BluntNouns))
                return Sets.GreatWeaponSmash;
            if (set == Sets.ToxicMelee && ContainsAny(weaponName, ClawNouns))
                return Sets.ToxicRend;
            return set;
        }

        private static bool ContainsAny(string name, string[] words)
        {
            foreach (string word in words)
                if (name.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // #379: aerial bombing runs are range-0 weapons carrying Strafing (Bombing Run, Drop Bombs,
        // Fire Bombs... 21 AoF + 12 GDF refs). No name keyword unites them and their payload words
        // would misread ("Flame Bombs" is not a flame sword) - the RULE is the signature.
        private static bool CarriesStrafing(WeaponFileEntry weapon)
        {
            static bool IsStrafing(SpecialRuleEntry rule) => rule switch
            {
                SpecialRuleEntry_Core core   => core.Name.Equals("Strafing", StringComparison.OrdinalIgnoreCase),
                SpecialRuleEntry_Alias alias => IsStrafing(alias.AliasedRule),
                _                            => false,
            };
            foreach (SpecialRuleEntry rule in weapon.SpecialRules)
                if (IsStrafing(rule)) return true;
            return false;
        }

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

        // Per-faction default sets for the 40 Age of Fantasy books (#378 survey, 2026-08-23). A separate
        // table, not new rows: four AoF faction names (Change/Lust/Plague/War Disciples) COLLIDE with GDF
        // factions, which is exactly how the first AoF bake picked up sci-fi tracer fire - the game
        // system must be part of the key. Keys are the books' Faction strings.
        private static readonly Dictionary<string, (string Ranged, string Melee)> AofFactionDefaultsTable = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Beastmen"]                     = (Sets.ArrowLoose, Sets.BeastMaw),
            ["Change Disciples"]             = (Sets.ArcaneBolt, Sets.BladeStandard),
            ["Chivalrous Kingdoms"]          = (Sets.ArrowLoose, Sets.BladeStandard),
            ["Dark Elves"]                   = (Sets.CrossbowBolt, Sets.BladeStandard),
            ["Deep-Sea Elves"]               = (Sets.ThrownSpear, Sets.SpearPierce),
            ["Dragon Empire"]                = (Sets.ArrowLoose, Sets.BladeStandard),
            ["Duchies of Vinci"]             = (Sets.CrossbowBolt, Sets.BladeStandard),
            ["Dwarves"]                      = (Sets.BallisticSlug, Sets.CrudeMelee),
            ["Eternal Wardens"]              = (Sets.ArrowLoose, Sets.SpearPierce),
            ["Ghostly Undead"]               = (Sets.ArcaneBolt, Sets.SpectralTouch),
            ["Giant Tribes"]                 = (Sets.SlingStone, Sets.TitanImpact),
            ["Giant Tribes Change Disciples"] = (Sets.SlingStone, Sets.TitanImpact),
            ["Giant Tribes Lust Disciples"]  = (Sets.SlingStone, Sets.TitanImpact),
            ["Giant Tribes Plague Disciples"] = (Sets.SlingStone, Sets.TitanImpact),
            ["Giant Tribes War Disciples"]   = (Sets.SlingStone, Sets.TitanImpact),
            ["Goblins"]                      = (Sets.ArrowLoose, Sets.CrudeMelee),
            ["Halflings"]                    = (Sets.SlingStone, Sets.BladeStandard),
            ["Havoc Dwarves"]                = (Sets.BallisticSlug, Sets.CrudeMelee),
            ["Havoc Warriors"]               = (Sets.ArcaneBolt, Sets.BladeStandard),
            ["High Elves"]                   = (Sets.ArrowLoose, Sets.BladeStandard),
            ["Human Empire"]                 = (Sets.BallisticSlug, Sets.BladeStandard),
            ["Kingdom of Angels"]            = (Sets.CrossbowBolt, Sets.BladeStandard),
            ["Lust Disciples"]               = (Sets.ArcaneBolt, Sets.BladeStandard),
            ["Mummified Undead"]             = (Sets.ArrowLoose, Sets.BladeStandard),
            ["Ogres"]                        = (Sets.SlingStone, Sets.CrudeMelee),
            ["Orcs"]                         = (Sets.ArrowLoose, Sets.CrudeMelee),
            ["Ossified Undead"]              = (Sets.ArrowLoose, Sets.BladeStandard),
            ["Plague Disciples"]             = (Sets.ArcaneBolt, Sets.ToxicMelee),
            ["Ratmen"]                       = (Sets.SlingStone, Sets.BladeStandard),
            ["Rift Daemons of Change"]       = (Sets.ArcaneBolt, Sets.DaemonArcaneMelee),
            ["Rift Daemons of Lust"]         = (Sets.ArcaneBolt, Sets.DaemonArcaneMelee),
            ["Rift Daemons of Plague"]       = (Sets.BioOrganic, Sets.ToxicMelee),
            ["Rift Daemons of War"]          = (Sets.ArcaneBolt, Sets.DaemonArcaneMelee),
            ["Saurians"]                     = (Sets.ThrownSpear, Sets.ClawRend),
            ["Shadow Stalkers"]              = (Sets.ThrownSpear, Sets.ClawRend),
            ["Sky-City Dwarves"]             = (Sets.BallisticSlug, Sets.CrudeMelee),
            ["Vampiric Undead"]              = (Sets.ArrowLoose, Sets.BladeStandard),
            ["Volcanic Dwarves"]             = (Sets.BreathFlame, Sets.CrudeMelee),
            ["War Disciples"]                = (Sets.ArcaneBolt, Sets.BladeStandard),
            ["Wood Elves"]                   = (Sets.ArrowLoose, Sets.BladeStandard),
        };

        /// <summary>The faction's default (ranged, melee) sets for a game system (#378: null/absent
        /// means Grimdark Future), or (null, null) for a faction that system's table doesn't know
        /// (hand-authored books/armies) — the front-end global default applies.</summary>
        public static (string? Ranged, string? Melee) FactionDefaults(string faction, string? gameSystem = null)
        {
            var table = IsAof(gameSystem) ? AofFactionDefaultsTable : FactionDefaultsTable;
            return table.TryGetValue(faction ?? string.Empty, out var d) ? (d.Ranged, d.Melee) : (null, null);
        }

        private static bool IsAof(string? gameSystem) =>
            string.Equals(GameSystems.Normalize(gameSystem), GameSystems.AgeOfFantasy, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The keyword/override match for a weapon name, or null when nothing distinctive matches
        /// (the army default covers it at load). <paramref name="faction"/> feeds the rare exact
        /// (faction, name) overrides; pass what the data has — an unknown faction just skips them.
        /// <paramref name="gameSystem"/> selects the keyword vocabulary (#378: null/absent = GDF).
        /// A range-0 weapon carrying Strafing reads as a bombing run before any name matching (#379).
        /// </summary>
        public static string? Match(string faction, WeaponFileEntry weapon, string? gameSystem = null) =>
            weapon.RangeInches <= 0 && CarriesStrafing(weapon)
                ? Sets.BombingRun
                : Match(faction, weapon.Name, isRanged: weapon.RangeInches > 0, gameSystem);

        /// <inheritdoc cref="Match(string, WeaponFileEntry, string?)"/>
        public static string? Match(string faction, string weaponName, bool isRanged, string? gameSystem = null)
        {
            if (string.IsNullOrWhiteSpace(weaponName)) return null;

            if (NameOverrides.TryGetValue((faction ?? string.Empty, weaponName.Trim()), out string? overridden))
                return overridden;

            (string Set, string[] Keywords)[] table = IsAof(gameSystem)
                ? (isRanged ? AofRangedKeywords : AofMeleeKeywords)
                : (isRanged ? RangedKeywords : MeleeKeywords);
            foreach ((string set, string[] keywords) in table)
                foreach (string keyword in keywords)
                    if (weaponName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        return isRanged ? set : UpgradeMeleeForm(set, weaponName);

            return null;
        }

        /// <summary>Stamps the faction's default sets onto a book (only where unset). Returns whether
        /// anything changed. Per-weapon keys are NOT written into books — they bake at compile time,
        /// so a keyword-table improvement reaches every future army without re-patching 47 books.</summary>
        public static bool ApplyToBook(BookFile book)
        {
            (string? ranged, string? melee) = FactionDefaults(book.Faction, book.GameSystem);
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

            (string? ranged, string? melee) = FactionDefaults(army.Faction, army.GameSystem);
            if (army.DefaultRangedEffectSet == null && ranged != null) { army.DefaultRangedEffectSet = ranged; changed = true; }
            if (army.DefaultMeleeEffectSet == null && melee != null) { army.DefaultMeleeEffectSet = melee; changed = true; }

            foreach (UnitFileEntry unit in army.Units)
            {
                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    if (weapon.EffectSet != null) continue;
                    string? match = Match(army.Faction, weapon, army.GameSystem);
                    if (match != null) { weapon.EffectSet = match; changed = true; }
                }
            }

            if (army is BuiltArmyFile built && built.Book != null)
                changed |= ApplyToBook(built.Book);

            return changed;
        }
    }
}
