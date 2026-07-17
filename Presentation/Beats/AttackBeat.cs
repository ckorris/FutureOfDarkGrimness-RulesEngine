using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A unit attacking another — shots in flight (ranged) or a clash (melee). Carries the
    /// attacker model positions (<see cref="From"/>) and the target model positions (<see cref="To"/>)
    /// so the front-end can animate between them; it renders differently per <see cref="IsMelee"/>
    /// (tracers vs. a clash). <see cref="From"/> holds one position per firing weapon, at its actual
    /// model — a volley fires all of them simultaneously, and <see cref="VolleyCount"/> volleys play
    /// one after another (so five A2 rifles show five together, then five more). <see cref="ArmorPenetration"/>
    /// scales their size. Emitted before the hit dice; the dice then show the resolution.
    /// </summary>
    [Serializable]
    public sealed class AttackBeat : PresentationBeat
    {
        public bool IsMelee { get; }

        /// <summary>One position per firing weapon, at its model — a volley fires all of these at once.</summary>
        public IReadOnlyList<Position> From { get; }
        public IReadOnlyList<Position> To { get; }
        public int VolleyCount { get; }       // volleys fired one after another (Attacks per weapon)
        public int ArmorPenetration { get; }

        /// <summary>
        /// #239: the firing weapon's effect-set key — an opaque identifier from the army data that
        /// selects the projectile/swing visual and sounds. Null means the front-end's global default.
        /// </summary>
        public string? WeaponEffect { get; }

        /// <summary>
        /// #239: natural to-hit successes and attacks rolled, so front-ends can land only the shots
        /// that actually hit and overshoot the misses. Floats — Realistic-mode dice are fractional.
        /// <see cref="AttackCount"/> &lt;= 0 means unknown: render the legacy all-shots-connect way.
        /// </summary>
        public float HitCount { get; }

        /// <inheritdoc cref="HitCount"/>
        public float AttackCount { get; }

        public AttackBeat(bool isMelee, IReadOnlyList<Position> from, IReadOnlyList<Position> to,
            int volleyCount, int armorPenetration,
            string? weaponEffect = null, float hitCount = 0f, float attackCount = 0f)
        {
            IsMelee = isMelee;
            From = from;
            To = to;
            VolleyCount = volleyCount;
            ArmorPenetration = armorPenetration;
            WeaponEffect = weaponEffect;
            HitCount = hitCount;
            AttackCount = attackCount;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ForVolleys(VolleyCount);

        // #238: held with a zero lead-in, so the presenter emits the attack and immediately proceeds
        // to the to-hit DiceRolledBeat that always follows it — front-ends animate the shots/swings
        // WHILE the dice tumble instead of after. The attack's duration (ForVolleys caps at 1600ms)
        // always fits inside the dice beat's envelope, so the dice pacing covers both.
        public override bool Held => true;
        public override TimeSpan HoldLeadIn => TimeSpan.Zero;

        // Purely visual — the surrounding hit/wound logs already narrate the outcome.
        public override string? Text => null;
    }
}
