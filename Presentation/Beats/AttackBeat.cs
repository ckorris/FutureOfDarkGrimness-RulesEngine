using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A unit attacking another — shots in flight (ranged) or a clash (melee). Carries the
    /// attacker model positions (<see cref="From"/>) and the target model positions (<see cref="To"/>)
    /// so the front-end can animate between them; it renders differently per <see cref="IsMelee"/>
    /// (tracers vs. a clash). Each volley fires <see cref="ShotsPerVolley"/> shots/strikes
    /// simultaneously (one per weapon in the group), and <see cref="VolleyCount"/> volleys play one
    /// after another — so five A2 rifles show five together, then five more. <see cref="ArmorPenetration"/>
    /// scales their size. Emitted before the hit dice; the dice then show the resolution.
    /// </summary>
    [Serializable]
    public sealed class AttackBeat : PresentationBeat
    {
        public bool IsMelee { get; }
        public IReadOnlyList<Position> From { get; }
        public IReadOnlyList<Position> To { get; }
        public int ShotsPerVolley { get; }   // weapons firing together (WeaponCount)
        public int VolleyCount { get; }       // volleys fired one after another (Attacks per weapon)
        public int ArmorPenetration { get; }

        public AttackBeat(bool isMelee, IReadOnlyList<Position> from, IReadOnlyList<Position> to,
            int shotsPerVolley, int volleyCount, int armorPenetration)
        {
            IsMelee = isMelee;
            From = from;
            To = to;
            ShotsPerVolley = shotsPerVolley;
            VolleyCount = volleyCount;
            ArmorPenetration = armorPenetration;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ForVolleys(VolleyCount);

        // Purely visual — the surrounding hit/wound logs already narrate the outcome.
        public override string? Text => null;
    }
}
