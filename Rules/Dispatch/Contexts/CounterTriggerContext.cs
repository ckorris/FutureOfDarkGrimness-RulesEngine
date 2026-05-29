using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Melee_OnCounterTrigger"/>: a charged defender that
    /// has Counter strikes first, before the charging unit's strikes resolve.
    /// <see cref="Attacker"/> is the charger; <see cref="Defender"/> is the Counter
    /// bearer.
    /// </summary>
    public sealed record CounterTriggerContext(IUnit Attacker, IUnit Defender) : IHookContext
    {
        public EHookID Hook => EHookID.Melee_OnCounterTrigger;
    }
}
