using FDG.SaveLoad;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.SaveLoad
{
    public class SpecialRuleRegistry
    {
        private static readonly SpecialRuleEntry_Core[] _core =
    {
        new SpecialRuleEntry_Core("Aircraft"),
        new SpecialRuleEntry_Core("Ambush"),
        new SpecialRuleEntry_Core("Counter"),
        new SpecialRuleEntry_Core("Entrenched"),
        new SpecialRuleEntry_Core("Fast"),
        new SpecialRuleEntry_Core("Fearless"),
        new SpecialRuleEntry_Core("Flying"),
        new SpecialRuleEntry_Core("Furious"),
        new SpecialRuleEntry_Core("Hero"),
        new SpecialRuleEntry_Core("Immobile"),
        new SpecialRuleEntry_Core("Indirect"),
        new SpecialRuleEntry_Core("Lance"),
        new SpecialRuleEntry_Core("Lock‑On"),
        new SpecialRuleEntry_Core("Limited"),
        new SpecialRuleEntry_Core("Poison"),
        new SpecialRuleEntry_Core("Regeneration"),
        new SpecialRuleEntry_Core("Relentless"),
        new SpecialRuleEntry_Core("Reliable"),
        new SpecialRuleEntry_Core("Rending"),
        new SpecialRuleEntry_Core("Scout"),
        new SpecialRuleEntry_Core("Slow"),
        new SpecialRuleEntry_Core("Sniper"),
        new SpecialRuleEntry_Core("Stealth"),
        new SpecialRuleEntry_Core("Strider")
    };

        //— rules that *require* a numeric parameter ————————————
        private static readonly SpecialRuleEntry_CoreNumeric[] _numeric =
        {
        new SpecialRuleEntry_CoreNumeric("Blast",       1),   //default X=1
        new SpecialRuleEntry_CoreNumeric("Caster",      1),
        new SpecialRuleEntry_CoreNumeric("Deadly",      1),
        new SpecialRuleEntry_CoreNumeric("Fear",        1),
        new SpecialRuleEntry_CoreNumeric("Impact",      1),
        new SpecialRuleEntry_CoreNumeric("Tough",       1),
        new SpecialRuleEntry_CoreNumeric("Transport",   1)
    };

        //Public read‑only accessors
        public static IReadOnlyList<SpecialRuleEntry_Core> CoreRules => _core;
        public static IReadOnlyList<SpecialRuleEntry_CoreNumeric> NumericRules => _numeric;

        //Convenience helpers ------------------------------------------------------

        /// <summary>Returns *all* core rules (numeric and non‑numeric) as printable names.</summary>
        public static IReadOnlyList<string> GetAllRuleNames()
        {
            var list = new List<string>(_core.Length + _numeric.Length);
            foreach (var c in _core) list.Add(c.PrintableName);
            foreach (var n in _numeric) list.Add(n.PrintableName); // e.g. "Blast(1)"
            return list;
        }

        /// <summary>
        /// Tries to create a new rule instance from a name the user picked.
        /// If the name belongs to the numeric family, uses <paramref name="value"/> (default 1).
        /// Returns <c>null</c> if the name is unknown (shouldn’t happen if you feed it via the list).
        /// </summary>
        public static SpecialRuleEntry? CreateRuleByName(string name, int value = 1)
        {
            //Exact match in non‑numeric list
            foreach (var c in _core)
                if (String.Equals(c.PrintableName, name, StringComparison.Ordinal))
                    return c;

            //Starts with numeric keyword?
            foreach (var n in _numeric)
                if (String.Equals(n.Name, name, StringComparison.Ordinal))
                    return new SpecialRuleEntry_CoreNumeric(n.Name, value);

            return null; //unknown
        }
    }
}
