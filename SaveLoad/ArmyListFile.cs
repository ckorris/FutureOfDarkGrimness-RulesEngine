using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using FDG.Rules.Definitions;

namespace FDG.SaveLoad
{
    [Serializable]
    public class ArmyListFile
    {
        public const string EXTENSION_NO_PERIOD = "fdgarmy";
        public const string EXTENSION_WITH_PERIOD = "." + EXTENSION_NO_PERIOD;

        public string Name { get; set; } = String.Empty;

        public string Faction { get; set; } = String.Empty;

        public int PointsLimit { get; set; }

        public List<UnitFileEntry> Units { get; set; } = new List<UnitFileEntry>();

        /// <summary>
        /// #239: the army's default effect-set keys, used for any weapon entry whose
        /// <see cref="WeaponFileEntry.EffectSet"/> is null — one for ranged weapons, one for melee.
        /// Opaque presentation identifiers; null falls through to the front-end's global default,
        /// and is omitted on save so pre-#239 files round-trip unchanged.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string? DefaultRangedEffectSet { get; set; }

        /// <inheritdoc cref="DefaultRangedEffectSet"/>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string? DefaultMeleeEffectSet { get; set; }

        /// <summary>
        /// #197 P17: unit specs this army can PLACE mid-game but which do not deploy with it — the
        /// targets of Spawn("Spores [5]") / Split("Changelings [10]"), keyed by the rule's exact text
        /// argument (each entry's Name IS that text). Compiled from the book by <c>ListCompiler</c>
        /// (the Forge path) or hand-authored; consumed at runtime by the unit-creation service via the
        /// army's persisted rule data. Null for almost every army and omitted from the file then, so
        /// existing files round-trip unchanged.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<UnitFileEntry>? AuxiliaryUnits { get; set; }

        /// <summary>
        /// Special-rule definitions that travel embedded with this army (#059). Registered into the
        /// game's rule resolver — core-first, then these, override-by-name — when the army loads, so a
        /// template's own/overriding rules are available before its units resolve their rule names.
        /// </summary>
        public List<SpecialRuleDefinition> RuleDefinitions { get; set; } = new List<SpecialRuleDefinition>();

        /// <summary>
        /// The army's spell list (#033): castable by any unit carrying Caster(X). Embedded in the army
        /// file and serialized with the same STJ kind-schema as <see cref="RuleDefinitions"/> (each spell's
        /// <c>Effect</c> graph is polymorphic). Resolved into runtime spells at army load.
        /// </summary>
        public List<SpellDefinition> Spells { get; set; } = new List<SpellDefinition>();

        /// <summary>Points that belong to the army but cannot be attributed to any single unit (#241/#219).
        /// An Army Forge share list reports per-unit costs as BASE costs and its true total separately in
        /// `listPoints`; the difference is upgrade points OPR prices internally and never publishes per
        /// option, so it is unattributable by construction. Carried here so an imported army's
        /// <see cref="TotalPoints"/> - and therefore force-org validation - matches Army Forge, instead of
        /// silently importing light. 0 for hand-authored and Forge-compiled armies.</summary>
        public int UnattributedPoints { get; set; }

        [JsonIgnore]
        public int TotalPoints
        {
            get
            {
                int total = UnattributedPoints;
                foreach (UnitFileEntry unit in Units)
                {
                    total += unit.PointCost;
                }
                return total;
            }
        }
    }
}
