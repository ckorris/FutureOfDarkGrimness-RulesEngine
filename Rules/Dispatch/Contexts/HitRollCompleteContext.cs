using System;
using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnHitRollComplete"/>: hit rolls are
    /// collected, before the save flow. Carries the unmodified hit rolls as an
    /// <see cref="IDiceResults"/> histogram (so natural-6 rules read
    /// <c>SpecialRuleHitRolls.At(6)</c> and stay correct under both the realistic
    /// and probabilistic rollers), plus the shooting distance for range-gated ones
    /// (Relentless &gt; 9").
    ///
    /// <see cref="IsMelee"/> distinguishes the shooting and melee uses of this hook
    /// (the hit-roll stages are shared) so melee-only rules (Furious) can gate on it;
    /// <see cref="IsCharging"/> further gates charge-only rules (Thrust's AP, modelled
    /// as a save modifier folded here).
    ///
    /// Fields still grow on demand — likely future additions: the weapon and its AP
    /// (Rending promotes AP on a 6).
    /// </summary>
    /// <param name="ChargeOriginDistanceInches">How far the defender was when the charging unit's
    /// activation began, i.e. where the charge was declared from. 0 for shooting and for a melee swing
    /// that isn't a charge. See <see cref="IHasAttackOriginDistance"/>.</param>
    /// <param name="TerrainPieces">The table's terrain pieces, for terrain-proximity conditions (the
    /// Grounded family). Null/empty on paths that cannot supply it; see <see cref="IHasTerrain"/>.</param>
    /// <param name="SixesCountForSpecialRules">#418 (Core Rules v3.6.0 Fatigue): false when the attacking
    /// unit counts as Fatigued and this is a melee attack, so its natural 6s still hit but "don't count as
    /// rolls of 6 for special rules" - <see cref="SpecialRuleHitRolls"/> then reads zero on face 6. Every
    /// builder of a melee hit context decides it through <c>FatigueUtilities.SixesCountForSpecialRules</c>;
    /// shooting, spells and synthetic hits are never Fatigued and keep the default.</param>
    /// <param name="AttackerMoved">#434: whether the attacking unit moved this activation - false is a Hold. OPR 3.6
    /// Slayer / Ranged Slayer give their AP(+2) "when using Hold actions and shooting", and AP folds at this hook, so
    /// the Hold test (<see cref="Condition.AfterMoving"/>, Artillery's and Indirect's) must be answerable here too.
    /// Synthetic hits (spells, abilities) never moved and keep the default.</param>
    /// <param name="HitRollNeeded">#433: the roll a hit needed on these dice - the attacker's Quality after every
    /// modifier, clamped to the die (2..6), the same number <c>RollToHitStage</c> splits successes from failures with.
    /// Read through <see cref="IHasHitRollNeeded"/> by rules that trigger only on a SUCCESSFUL natural face (OPR 3.6's
    /// "extra attacks on successful unmodified hit results of 5-6"). 0 (the default) means no threshold: every rolled
    /// die is a hit - the synthetic-hit pipeline's dice are hits by construction, and a forecast's empty histogram has
    /// nothing to gate. Every live hit roll (RollToHitStage, the AI's CombatMath) passes its real threshold.</param>
    public sealed record HitRollCompleteContext(
        IUnit Attacker, IUnit Target, IDiceResults UnmodifiedHitRolls,
        float DistanceInches = 0f, bool IsMelee = false, bool IsCharging = false, bool IsSpell = false,
        float ChargeOriginDistanceInches = 0f,
        EUnpredictableBranch UnpredictableBranch = EUnpredictableBranch.None,
        IReadOnlyList<ITerrain>? TerrainPieces = null,
        bool SixesCountForSpecialRules = true,
        bool AttackerMoved = false,
        int HitRollNeeded = 0)
        : IHookContext, IHasUnmodifiedHitRolls, IHasDistance, IHasCombatKind, IHasCharging, IHasTarget,
            IHasIsSpell, IHasAttackOriginDistance, IHasUnpredictableBranch, IHasTerrain, IHasShootChargeDistance,
            IHasAttackerMoved, IHasHitRollNeeded
    {
        public EHookID Hook => EHookID.Shooting_OnHitRollComplete;

        // A melee attack resolves in base contact, so DistanceInches is never a meaningful "how far did
        // this attack come from" in melee — the charge's declared distance is. Shooting launches from
        // where the shooter stands, so the two coincide.
        public float AttackOriginDistanceInches => IsMelee ? ChargeOriginDistanceInches : DistanceInches;

        // #432: a spell's hits are not a shooting attack ("when shot from over 9"" never reads a spell).
        public float? ShootingDistanceInches => IsMelee || IsSpell ? null : DistanceInches;

        public float? ChargeDistanceInches => IsMelee && IsCharging ? ChargeOriginDistanceInches : null;

        public IReadOnlyList<ITerrain> Terrain => TerrainPieces ?? Array.Empty<ITerrain>();

        // Computed on read rather than stored, so a `with` copy that flips the flag can never carry a stale
        // view. The common case (sixes count) allocates nothing.
        public IDiceResults SpecialRuleHitRolls =>
            SixesCountForSpecialRules ? UnmodifiedHitRolls : UnmodifiedHitRolls.WithoutFaces(SixFace);

        private static readonly int[] SixFace = { 6 };
    }
}
