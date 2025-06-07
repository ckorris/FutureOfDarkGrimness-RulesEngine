using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FDG.SaveLoad
{
    public static class ArmyListParser
    {
        //Regex that recognises a unit header line.
        private static readonly Regex UnitHeaderRx = new Regex(
            @"^(?<name>.+?)\s*\[(?<cnt>\d+)\]\s*Q(?<q>\d+)\+\s*D(?<d>\d+)\+\s*\|\s*(?<pts>\d+)pts(?:\s*\|\s*(?<rules>.+))?$",
            RegexOptions.Compiled);

        //Regex for weapon profiles.
        private static readonly Regex WeaponRx = new Regex(
            @"(?:(?<qty>\d+)x\s*)?(?<name>[^(]+?)\s*\((?<stats>[^)]*?)\)",
            RegexOptions.Compiled);

        //------------------------------------------------------------------------
        public static ArmyListFile Parse(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("Input is empty.", nameof(raw));

            string[] lines = Regex.Split(raw.Trim(), @"\r?\n");

            if (lines.Length == 0)
                throw new FormatException("No lines found.");

            //------------- header (first line)
            var army = new ArmyListFile();
            ParseHeaderLine(lines[0], army);

            //------------- iterate through remaining lines
            List<string> currentUnitLines = new List<string>();

            for (int i = 1; i < lines.Length; ++i)
            {
                string line = lines[i].Trim();

                //skip empty or joined markers
                if (line.Length == 0 || line.StartsWith("| Joined to:", StringComparison.Ordinal))
                    continue;

                if (UnitHeaderRx.IsMatch(line))
                {
                    //flush previous unit, if any
                    if (currentUnitLines.Count > 0)
                        army.Units.Add(ParseUnitBlock(currentUnitLines));

                    currentUnitLines.Clear();
                }

                currentUnitLines.Add(line);
            }

            if (currentUnitLines.Count > 0)
                army.Units.Add(ParseUnitBlock(currentUnitLines));

            return army;
        }

        //------------------------------------------------------------------------
        private static void ParseHeaderLine(string line, ArmyListFile army)
        {
            var rx = new Regex(@"\+\+\s*(?<faction>.+?)\s*(?:\(v[^\)]*\))?" +
                               @"\s*\[GF\s*(?<pts>\d+)pts\]\s*\+\+",
                               RegexOptions.IgnoreCase);

            Match m = rx.Match(line);
            if (!m.Success) throw new FormatException("Header not recognised.");

            army.Faction = m.Groups["faction"].Value.Trim();
            army.Name = army.Faction + " List";
            army.PointsLimit = Int32.Parse(m.Groups["pts"].Value);
        }

        //------------------------------------------------------------------------
        private static UnitFileEntry ParseUnitBlock(List<string> blockLines)
        {
            if (blockLines.Count == 0)
                throw new FormatException("Empty unit block.");

            Match h = UnitHeaderRx.Match(blockLines[0]);
            if (!h.Success)
                throw new FormatException("Bad unit header: " + blockLines[0]);

            var unit = new UnitFileEntry
            {
                Name = h.Groups["name"].Value.Trim(),
                ModelCount = Int32.Parse(h.Groups["cnt"].Value),
                Quality = Int32.Parse(h.Groups["q"].Value),
                Defense = Int32.Parse(h.Groups["d"].Value),
                PointCost = Int32.Parse(h.Groups["pts"].Value)
            };

            if (h.Groups["rules"].Success)
                foreach (string r in h.Groups["rules"].Value.Split(','))
                    unit.SpecialRules.Add(r.Trim());

            //everything after the header line is treated as weapon text
            string weaponText = String.Join(" ", blockLines.GetRange(1, blockLines.Count - 1));
            ParseWeapons(weaponText, unit);

            return unit;
        }

        //------------------------------------------------------------------------
        private static void ParseWeapons(string text, UnitFileEntry unit)
        {
            foreach (Match mw in WeaponRx.Matches(text))
            {
                var w = new WeaponFileEntry
                {
                    Quantity = mw.Groups["qty"].Success
                             ? Int32.Parse(mw.Groups["qty"].Value) : 1,
                    Name = mw.Groups["name"].Value.Trim()
                };

                foreach (string token in mw.Groups["stats"].Value.Split(','))
                {
                    string t = token.Trim();
                    if (t.EndsWith("\"") && Int32.TryParse(t.TrimEnd('"'), out int rng))
                        w.RangeInches = rng;
                    else if (t.StartsWith("A", StringComparison.OrdinalIgnoreCase) &&
                             Int32.TryParse(t.Substring(1), out int atk))
                        w.Attacks = atk;
                    else if (t.StartsWith("AP(", StringComparison.OrdinalIgnoreCase) &&
                             Int32.TryParse(t.Substring(3, t.Length - 4), out int ap))
                        w.ArmorPenetration = ap;
                    else if (!String.IsNullOrWhiteSpace(t))
                        w.SpecialRules.Add(t);
                }
                unit.Weapons.Add(w);
            }
        }
    }
}
