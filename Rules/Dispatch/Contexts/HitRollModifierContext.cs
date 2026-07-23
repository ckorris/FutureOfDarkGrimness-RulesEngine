using System;
using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnHitRollModifier"/>: adjusting Quality
    /// or per-roll modifiers before rolling to hit. Carries the attacker-to-target
    /// distance (Stealth &gt; 9"), whether the attacker moved this activation
    /// (Indirect's -1 after moving), whether the attack is melee (the hit-roll stages
    /// are shared with shooting), and whether the attacker is charging (Thrust's +1 to hit).
    /// </summary>
    /// <param name="ChargeOriginDistanceInches">How far the defender was when the charging unit's activation
    /// began. 0 for shooting and for a non-charge melee swing. See <see cref="IHasAttackOriginDistance"/>:
    /// the "shot or charged from over 9\" away" defensive rules (Changebound, Guarded, ...) gate here, and a
    /// live-distance check can never see a charge because melee resolves in base contact.</param>
    /// <param name="TerrainPieces">The table's terrain pieces, for terrain-proximity conditions (the
    /// Grounded family). Null/empty on paths that cannot supply it (AI valuation, synthetic hits); see
    /// <see cref="IHasTerrain"/>.</param>
    public sealed record HitRollModifierContext(
        IUnit Attacker, IUnit Target, float DistanceInches, bool AttackerMoved = false,
        bool IsMelee = false, bool IsCharging = false, float ChargeOriginDistanceInches = 0f,
        EUnpredictableBranch UnpredictableBranch = EUnpredictableBranch.None,
        IReadOnlyList<ITerrain>? TerrainPieces = null)
        : IHookContext, IHasDistance, IHasAttackerMoved, IHasCombatKind, IHasCharging, IHasTarget,
            IHasAttackOriginDistance, IHasUnpredictableBranch, IHasTerrain
    {
        public EHookID Hook => EHookID.Shooting_OnHitRollModifier;

        public float AttackOriginDistanceInches => IsMelee ? ChargeOriginDistanceInches : DistanceInches;

        public IReadOnlyList<ITerrain> Terrain => TerrainPieces ?? Array.Empty<ITerrain>();
    }
}
