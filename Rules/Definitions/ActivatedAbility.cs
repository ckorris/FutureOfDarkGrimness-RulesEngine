using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// A player-triggered ability or spell. Offered to the player when the engine
/// fires the <see cref="TriggerHook"/> hook (typical: <see cref="EHookID.Activation_OnPreAttack"/>
/// for "before attacking, you may use this"). If the player accepts and can
/// pay the <see cref="Cost"/>, the engine resolves <see cref="TargetSelector"/>
/// to ask the player to pick target(s), then queues the <see cref="Effect"/>
/// against the picked targets.
///
/// <see cref="AvailableWhen"/> gates whether the option is offered at all —
/// useful for rules that should only appear under specific game state (e.g.
/// "only offer if the unit isn't already Shaken"). For "are there any valid
/// targets in range" the <see cref="TargetSelector"/> already handles that;
/// reserve <see cref="AvailableWhen"/> for orthogonal availability conditions.
///
/// Spells are activated abilities with <see cref="Cost.SpellTokens"/> as their
/// Cost. Once-per-game / once-per-activation / once-per-round abilities use
/// the corresponding <see cref="Cost"/> subtypes; under the hood these become
/// token grants and consumes via the dispatcher.
/// </summary>
public record ActivatedAbility(EHookID TriggerHook, Cost Cost, TargetSelector TargetSelector,
    Effect Effect, Condition AvailableWhen);
