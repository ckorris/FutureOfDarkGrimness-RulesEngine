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

        [JsonIgnore]
        public int TotalPoints
        {
            get
            {
                int total = 0;
                foreach (UnitFileEntry unit in Units)
                {
                    total += unit.PointCost;
                }
                return total;
            }
        }
    }
}
