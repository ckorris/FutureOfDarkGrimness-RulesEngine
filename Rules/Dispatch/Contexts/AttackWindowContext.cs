using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Combat_OnAttackWindow"/>: <see cref="Unit"/> is about to attack
    /// <see cref="Defender"/> and nothing has been rolled yet. The offer site for one-shot extra attacks
    /// (#197 P16 Takedown Strike / Takedown Shot).
    ///
    /// <para>Carries the combat kind because the two corpus rules differ only in it - one fires when the
    /// bearer shoots, the other when it fights - so each is authored with <see cref="Condition.IsMelee"/>
    /// (or its negation) against this one hook rather than needing a hook each.</para>
    /// </summary>
    public sealed record AttackWindowContext(IUnit Unit, IUnit Defender, bool IsMelee)
        : IHookContext, IHasActingUnit, IHasTarget, IHasCombatKind
    {
        public EHookID Hook => EHookID.Combat_OnAttackWindow;

        public IUnit ActingUnit => Unit;

        public IUnit Target => Defender;
    }
}
