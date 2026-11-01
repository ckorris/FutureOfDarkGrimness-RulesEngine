using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Casting_OnSpellCastAttempt"/>: a Caster is about to make its casting roll.
    /// The hook the rulebook's "+1 to casting rolls" rules ride (Core Rules v3.6.0 Spell Amplifier); the
    /// cast stage folds every <see cref="Definitions.RuleOperation.ApplyRollModifier"/> of
    /// <see cref="Foundation.ERollKind.Cast"/> into the cast threshold, beside the boost / assist / granted /
    /// relay modifiers it already sums. Carries the casting unit only - the spell is not picked yet when the
    /// pre-pick odds preview asks, and no 3.6 casting modifier gates on it (#429 can widen this record when one
    /// does). Evaluated with the caster's own rules, its models' rules AND each of its weapons' rules, so a
    /// Caster Staff's Spell Amplifier counts. See <see cref="CastRollRules"/>.
    /// </summary>
    public sealed record SpellCastAttemptContext(IUnit Caster) : IHookContext
    {
        public EHookID Hook => EHookID.Casting_OnSpellCastAttempt;
    }
}
