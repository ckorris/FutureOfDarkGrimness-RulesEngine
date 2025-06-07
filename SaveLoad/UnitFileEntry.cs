using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FDG.SaveLoad
{
    [Serializable]
    public class UnitFileEntry
    {
        public int StableID { get; } = _nextID++;

        private static int _nextID = 1;

        public string Name { get; set; } = String.Empty;

        public int ModelCount { get; set; }

        public int Quality { get; set; }

        public int Defense { get; set; }

        public List<SpecialRuleEntry> SpecialRules { get; set; } = new List<SpecialRuleEntry>();

        public List<WeaponFileEntry> Weapons { get; set; } = new List<WeaponFileEntry>();

        public int PointCost { get; set; }
    }
}
