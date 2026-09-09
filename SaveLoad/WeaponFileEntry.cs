using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FDG.SaveLoad
{
    [Serializable]
    public class WeaponFileEntry
    {
        // #191: Interlocked, not `_nextID++`. Army files are deserialized concurrently by the lab
        // (games run in parallel) and by the search; a non-atomic increment hands two entries the
        // same StableID, which is the key other data points at.
        public int StableID { get; } = System.Threading.Interlocked.Increment(ref _nextID) - 1;

        private static int _nextID = 1;

        public string Name { get; set; } = String.Empty;

        public int Quantity { get; set; } = 1;

        public int RangeInches { get; set; }

        public int Attacks { get; set; }

        public int ArmorPenetration { get; set; }

        /// <summary>
        /// #239: this weapon's effect-set key — an opaque presentation identifier (e.g.
        /// "plasma-bolt") the front-end maps to a projectile/swing visual and sounds. Null falls
        /// back to the army's default for the weapon's ranged/melee kind
        /// (<see cref="ArmyListFile.DefaultRangedEffectSet"/> / <see cref="ArmyListFile.DefaultMeleeEffectSet"/>).
        /// The engine never interprets the value. Null is omitted on save (both serializers) so
        /// keyless entries stay byte-identical to pre-#239 files.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? EffectSet { get; set; }

        public List<SpecialRuleEntry> SpecialRules { get; set; } = new List<SpecialRuleEntry>();
    }
}
