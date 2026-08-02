using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnSaveRollModifier"/>: the attacker-only "weapon sight
    /// properties" context. Rules react here to queue target-independent weapon flags — Blast queues
    /// <see cref="RuleOperation.IgnoreCover"/>, and Indirect queues
    /// <see cref="RuleOperation.IgnoreLineOfSight"/>.
    ///
    /// These are properties of the attacker's weapon, independent of any specific target, so this context
    /// carries only the attacker. It's evaluated by <see cref="SightRuleQueries"/> at several sites — the
    /// cover stage (to drop the bonus), the occlusion stage (to keep a non-LoS target shootable), and the
    /// targeting/movement option builders (to flag the weapon as cover-/LoS-ignoring for the resolvers).
    /// </summary>
    public sealed record CoverIgnoreContext(IUnit Attacker) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnSaveRollModifier;
    }
}
