using System.Collections.Generic;
using System.Linq;

namespace FDG.ArmyBuilding
{
    /// <summary>
    /// #438 D3 (owner request, 2026-11-01): one Replace purchase the compiler could NOT land, with why. A Replace
    /// the loadout cannot afford is parked and retried after every section has applied (#323); when the retry
    /// still finds no target it is silently neither applied nor charged - the only symptom was a points total
    /// that disagreed with Army Forge. This names the conflict so the player can fix the pick instead of hunting
    /// for the mismatch. Reporting only: nothing here changes what compiles or what is charged.
    /// </summary>
    public sealed class UnappliedReplace
    {
        /// <summary>The roster unit's name as compiled ("Assault Brothers").</summary>
        public required string UnitName { get; init; }

        /// <summary>The section as the book labels it ("Replace one Heavy Pistol and Hand Weapon"), or a
        /// "Replace X and Y" built from its targets when the label is bare.</summary>
        public required string SectionLabel { get; init; }

        /// <summary>What the option would have gained, by name ("Heavy Pistol, Energy Hammer"); the option's
        /// label when it gains nothing nameable; empty when there is nothing to say.</summary>
        public required string Gains { get; init; }

        /// <summary>Applications still owed when the retry loop gave up. 0 for a "Replace all" that found no
        /// target at all (its count is "every match", which is nothing here).</summary>
        public required int Shortfall { get; init; }

        /// <summary>The targets the loadout ran out of, as "N more X" ("1 more Hand Weapon"), one per scarce
        /// target. For a "Replace all" with nothing to replace: the bare target names.</summary>
        public required IReadOnlyList<string> Missing { get; init; }

        /// <summary>The one-line explanation, ASCII:
        /// <c>Assault Brothers: 'Replace one Heavy Pistol and Hand Weapon -> Heavy Pistol, Energy Hammer' not applied
        /// (needs 1 more Hand Weapon)</c>; a "Replace all" with no target reads
        /// <c>... not applied (the unit has no Sword)</c>.</summary>
        public string Message
        {
            get
            {
                string pick = Gains.Length > 0 ? $"{SectionLabel} -> {Gains}" : SectionLabel;
                string why = Shortfall > 0
                    ? "needs " + string.Join(", ", Missing)
                    : "the unit has no " + string.Join(" or ", Missing);
                return $"{UnitName}: '{pick}' not applied ({why})";
            }
        }

        public override string ToString() => Message;

        /// <summary>The lines for one unit, by name - the Forge keys its reconciliation rows by unit name.</summary>
        public static IEnumerable<UnappliedReplace> ForUnit(IEnumerable<UnappliedReplace> all, string unitName) =>
            all.Where(u => u.UnitName == unitName);
    }
}
