using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FDG.SaveLoad
{
    [Serializable]
    public class ArmyListFile
    {
        public string Name { get; set; } = String.Empty;

        public string Faction { get; set; } = String.Empty;

        public int PointsLimit { get; set; }

        public List<UnitFileEntry> Units { get; set; } = new List<UnitFileEntry>();

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
