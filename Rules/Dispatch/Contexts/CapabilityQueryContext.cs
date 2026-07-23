using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Lifecycle_OnCapabilityQuery"/>: "can <see cref="Unit"/> cast spells?".
    /// Carries only the unit being asked about — the question is about the unit itself, not about any
    /// spell, target, or roll, all of which come later (see the other <c>Casting_*</c> hooks).
    ///
    /// Read through <see cref="CapabilityRuleQueries"/>, never fired for its side effects: a rule that
    /// confers casting emits <see cref="Definitions.RuleOperation.EnableCasting"/> and nothing applies it.
    /// </summary>
    public sealed record CapabilityQueryContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Lifecycle_OnCapabilityQuery;
    }
}
