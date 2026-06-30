using System;
using System.Linq;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Detects the Aircraft special rule on a unit, for the out-of-combat gates it imposes (#029): it can't
    /// seize objectives, can't be charged / moved into base contact with, and deploys before all other units.
    /// A plain <see cref="IUnit.RuleDefinitions"/> name check — these gates run in stage code that has no
    /// <see cref="RuleEvaluator"/> in scope (mirrors <see cref="TransportUtilities.IsTransport"/>). Name-based
    /// (not reference equality) so an army's own embedded "Aircraft" rule is recognised too, and
    /// case-insensitive like the rule resolver.
    /// </summary>
    public static class AircraftRules
    {
        public static bool IsAircraft(IUnit unit) =>
            unit.RuleDefinitions.Any(rule =>
                string.Equals(rule.Definition.Name, "Aircraft", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rule.RequestedName, "Aircraft", StringComparison.OrdinalIgnoreCase));
    }
}
