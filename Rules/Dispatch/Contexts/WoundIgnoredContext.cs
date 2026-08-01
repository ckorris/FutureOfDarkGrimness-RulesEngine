using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Lifecycle_OnWoundIgnored"/>: <see cref="Defender"/> just absorbed
    /// <see cref="IgnoredWoundCount"/> wounds through a wound-ignore rule (Regeneration, Resistance)
    /// rather than taking them. Trigger for rules that COUNT ignored wounds — #197 P12 Regenerative
    /// Strength's markers.
    ///
    /// <para>The hook was declared and documented long before it had a context or a fire site; a rule
    /// authored at it validated, linted clean and never fired (the Breath Attack shape). This type and
    /// <c>AssignWoundsStage</c>'s fire site are what light it.</para>
    ///
    /// <para>The bearer sits in the <see cref="ERuleSeat.Subject"/> seat, like the other defender-side
    /// rules on the save-complete path: the unit that ignores the wound is the one being attacked. On the
    /// passive path <see cref="RuleInvocation.EffectiveTarget"/> is the bearer, so a token-granting effect
    /// here lands on the ignoring unit, which is what the rule wants.</para>
    /// </summary>
    public sealed record WoundIgnoredContext(IUnit Defender, IUnit Attacker, float IgnoredWoundCount)
        : IHookContext, IHasIgnoredWoundCount
    {
        public EHookID Hook => EHookID.Lifecycle_OnWoundIgnored;
    }
}
