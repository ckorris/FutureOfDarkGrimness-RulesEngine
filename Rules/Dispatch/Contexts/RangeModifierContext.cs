using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnRangeCheck"/>: a capability read for how far a weapon may
    /// effectively shoot. Carries only the attacker (the acting unit); the firing weapon and the defending
    /// unit are supplied as participants to <see cref="RangeRuleQueries.EffectiveRangeDelta"/>, so both the
    /// attacker's own range buffs (Increased Shooting Range, at the Actor seat) and the defender's range
    /// debuffs (Ranged Shrouding, at the Subject seat) fold into one delta. Mirrors
    /// <see cref="CoverIgnoreContext"/>, but range is target-dependent so the defender rides as a participant
    /// rather than the cover query's attacker-only form.
    /// </summary>
    public sealed record RangeModifierContext(IUnit Attacker) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnRangeCheck;
    }
}
