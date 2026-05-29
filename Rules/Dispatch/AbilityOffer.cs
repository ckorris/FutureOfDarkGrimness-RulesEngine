using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// A player-triggered ability surfaced as available at a hook. Distinct from a
    /// <see cref="Definitions.RuleOperation"/> on purpose: an operation is a resolved,
    /// deterministic action the engine will execute, whereas an offer is a
    /// pre-decision request — nothing is chosen, no cost paid, no randomness rolled
    /// yet. The bus gathers offers (see <see cref="IRuleHookBus.GatherOffers"/>) so the
    /// engine can present them to the player; once accepted with targets, resolution
    /// (see <see cref="IRuleHookBus.ResolveAbility"/>) produces the operation queue.
    /// </summary>
    /// <param name="Bearer">The unit whose rule provides the ability.</param>
    /// <param name="RuleName">The rule the ability came from (display + identity).</param>
    /// <param name="Ability">The full ability — Cost, TargetSelector, Effect, TriggerHook.</param>
    public sealed record AbilityOffer(IUnit Bearer, string RuleName, ActivatedAbility Ability);
}
