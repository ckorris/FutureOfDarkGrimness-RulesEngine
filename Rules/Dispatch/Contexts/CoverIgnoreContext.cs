using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnSaveRollModifier"/>: adjusting the defender's save for
    /// cover/AP-style effects. Blast reacts here to queue <see cref="RuleOperation.IgnoreCover"/>.
    ///
    /// Cover-ignore is a property of the attacker's weapon, independent of any specific target, so this
    /// context carries only the attacker — it's evaluated both at the cover stage (to drop the bonus) and
    /// while building targeting/movement options (to flag the weapon as cover-ignoring).
    /// </summary>
    public sealed record CoverIgnoreContext(IUnit Attacker) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnSaveRollModifier;
    }
}
