using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A unit attacking another — shots in flight (ranged) or a clash (melee). Carries the
    /// attacker model positions (<see cref="From"/>) and the target model positions (<see cref="To"/>)
    /// so the front-end can animate between them; it renders differently per <see cref="IsMelee"/>
    /// (tracers vs. a clash). <see cref="AttackCount"/> individual shots/strikes play one after
    /// another (so an A3 weapon shows three), and <see cref="ArmorPenetration"/> scales their size.
    /// Emitted before the hit dice; the dice then show the resolution.
    /// </summary>
    [Serializable]
    public sealed class AttackBeat : PresentationBeat
    {
        public bool IsMelee { get; }
        public IReadOnlyList<Position> From { get; }
        public IReadOnlyList<Position> To { get; }
        public int AttackCount { get; }
        public int ArmorPenetration { get; }

        public AttackBeat(bool isMelee, IReadOnlyList<Position> from, IReadOnlyList<Position> to,
            int attackCount, int armorPenetration)
        {
            IsMelee = isMelee;
            From = from;
            To = to;
            AttackCount = attackCount;
            ArmorPenetration = armorPenetration;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ForAttack(AttackCount);

        // Purely visual — the surrounding hit/wound logs already narrate the outcome.
        public override string? Text => null;
    }
}
