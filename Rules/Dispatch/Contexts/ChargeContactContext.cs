using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Melee_OnChargeContact"/>: the charging unit has
    /// reached combat, before any strikes. Impact (the charger, Actor seat), Brace (the
    /// charged unit, Subject seat; #422) and Ravage fire here — so "when charging" is
    /// implied by the hook itself, no separate condition needed.
    ///
    /// Minimal (the two combatants): per-model counts are read off the bearer by the
    /// effects themselves (Brace, Ravage).
    /// </summary>
    public sealed record ChargeContactContext(IUnit Attacker, IUnit Defender) : IHookContext
    {
        public EHookID Hook => EHookID.Melee_OnChargeContact;
    }
}
