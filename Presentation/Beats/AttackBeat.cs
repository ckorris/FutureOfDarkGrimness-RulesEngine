using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A unit attacking another — shots in flight (ranged) or a clash (melee). Carries the
    /// attacker model positions (<see cref="From"/>) and the target model positions (<see cref="To"/>)
    /// so the front-end can animate between them; it renders differently per <see cref="IsMelee"/>
    /// (tracers vs. a clash). Emitted before the hit dice; the dice then show the resolution.
    /// </summary>
    [Serializable]
    public sealed class AttackBeat : PresentationBeat
    {
        public bool IsMelee { get; }
        public IReadOnlyList<Position> From { get; }
        public IReadOnlyList<Position> To { get; }

        public AttackBeat(bool isMelee, IReadOnlyList<Position> from, IReadOnlyList<Position> to)
        {
            IsMelee = isMelee;
            From = from;
            To = to;
        }

        public override TimeSpan NominalDuration =>
            IsMelee ? PresentationDurations.MeleeClash : PresentationDurations.Projectiles;

        // Purely visual — the surrounding hit/wound logs already narrate the outcome.
        public override string? Text => null;
    }
}
