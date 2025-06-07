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
        public int StableID { get; } = _nextID++;

        private static int _nextID = 1;

        public string Name { get; set; } = String.Empty;

        public int Quantity { get; set; } = 1;

        public int RangeInches { get; set; }

        public int Attacks { get; set; }

        public int ArmorPenetration { get; set; }

        public List<SpecialRuleEntry> SpecialRules { get; set; } = new List<SpecialRuleEntry>();
    }
}
