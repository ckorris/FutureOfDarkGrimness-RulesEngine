using System;
using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnSaveRollComplete"/>: after defense
    /// rolls, before applying wounds. Carries the unmodified save rolls as an
    /// <see cref="IDiceResults"/> histogram (so rules reacting to natural results,
    /// e.g. Bane re-rolling unmodified Defense 6s, read <c>UnmodifiedSaveRolls.At(6)</c>
    /// and stay correct under the probabilistic roller). Attacker is the source of
    /// the attack; Defender holds the saves.
    ///
    /// <see cref="IsMelee"/> distinguishes the shooting and melee uses of this hook
    /// (the save flow is shared) so combat-kind-gated save-side rules (e.g. a
    /// "when shooting" Shred/Bane/Unstoppable variant) can read <c>Not(IsMelee)</c>
    /// — the same threading <see cref="HitRollCompleteContext"/> has on the hit side.
    /// <see cref="IsSpell"/> marks wounds resolved through the spell-damage pipeline
    /// (spell-facet rules like Resistance's ignore-on-2+ read <c>IsSpell</c>), the
    /// same flag <see cref="HitRollCompleteContext"/> carries on the hit side.
    /// </summary>
    /// <param name="DistanceInches">Attacker-to-defender distance, carried so save-side rules can be
    /// range-gated the way hit-side ones already are (#197). The wound-side Boost rules read it through
    /// <see cref="IHasAttackOriginDistance"/> rather than directly.</param>
    /// <param name="ChargeOriginDistanceInches">How far the defender was when the charging unit's
    /// activation began. 0 for shooting and for a non-charge melee swing.</param>
    /// <param name="TerrainPieces">Table terrain for terrain-proximity conditions (#376 Grounded
    /// Protection). Null/empty on paths that cannot supply it (AI volley valuation); see
    /// <see cref="IHasTerrain"/>.</param>
    public sealed record SaveRollCompleteContext(
        IUnit Attacker, IUnit Defender, IDiceResults UnmodifiedSaveRolls, bool IsMelee = false,
        bool IsSpell = false, float DistanceInches = 0f, float ChargeOriginDistanceInches = 0f,
        IReadOnlyList<ITerrain>? TerrainPieces = null)
        : IHookContext, IHasTarget, IHasUnmodifiedSaveRolls, IHasCombatKind, IHasIsSpell, IHasDistance,
            IHasAttackOriginDistance, IHasTerrain
    {
        public EHookID Hook => EHookID.Shooting_OnSaveRollComplete;

        // The defender IS the target for target-keyed conditions (TargetMajorityHasTough etc.).
        IUnit IHasTarget.Target => Defender;

        // Mirrors HitRollCompleteContext: in melee the launch distance is the charge's declared distance,
        // never the base-contact distance the save is being rolled at.
        public float AttackOriginDistanceInches => IsMelee ? ChargeOriginDistanceInches : DistanceInches;

        public IReadOnlyList<ITerrain> Terrain => TerrainPieces ?? Array.Empty<ITerrain>();
    }
}
