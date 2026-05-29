using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Melee_OnChargeContact"/>: the charging unit has
    /// reached combat, before any strikes. The hook the rulebook names for Furious,
    /// Thrust, Impact and Counter — so "when charging" is implied by the hook itself,
    /// no separate condition needed.
    ///
    /// Minimal for now (the two combatants); likely to grow a who-charged / model
    /// count field when Impact and Counter need them.
    /// </summary>
    public sealed record ChargeContactContext(IUnit Attacker, IUnit Defender) : IHookContext
    {
        public EHookID Hook => EHookID.Melee_OnChargeContact;
    }
}
