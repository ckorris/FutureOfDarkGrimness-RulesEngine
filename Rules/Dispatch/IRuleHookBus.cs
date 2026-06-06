using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

public interface IRuleHookBus
{
    /// <summary>
    /// Fires a hook for passive rules: returns the resolved operation queue produced
    /// by every matching <see cref="HookEntry"/> on the relevant units.
    /// </summary>
    public IReadOnlyList<RuleOperation> Dispatch(IHookContext context);

    /// <summary>
    /// Gathers the player-triggered abilities available at this hook — abilities whose
    /// <see cref="ActivatedAbility.TriggerHook"/> matches, whose
    /// <see cref="ActivatedAbility.AvailableWhen"/> passes, and whose cost is currently
    /// affordable. Returns offers, not operations: nothing is resolved until the player
    /// accepts.
    /// </summary>
    public IReadOnlyList<AbilityOffer> GatherOffers(IHookContext context);

    /// <summary>
    /// Resolves an accepted ability against the chosen <paramref name="targets"/>: pays
    /// the cost and fires the effect, returning the combined operation queue
    /// (cost-consumption operations followed by effect operations).
    /// </summary>
    public IReadOnlyList<RuleOperation> ResolveAbility(AbilityOffer offer, IReadOnlyList<IUnit> targets);
}
