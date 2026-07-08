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
    public sealed record SaveRollCompleteContext(
        IUnit Attacker, IUnit Defender, IDiceResults UnmodifiedSaveRolls, bool IsMelee = false,
        bool IsSpell = false)
        : IHookContext, IHasTarget, IHasUnmodifiedSaveRolls, IHasCombatKind, IHasIsSpell
    {
        public EHookID Hook => EHookID.Shooting_OnSaveRollComplete;

        // The defender IS the target for target-keyed conditions (TargetMajorityHasTough etc.).
        IUnit IHasTarget.Target => Defender;
    }
}
