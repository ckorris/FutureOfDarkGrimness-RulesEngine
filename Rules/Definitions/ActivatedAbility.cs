using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// A player-triggered ability or spell. Offered to the player when the engine
/// fires the <see cref="TriggerHook"/> hook (typical: <see cref="EHookID.Activation_OnBeforeAttackAction"/>
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
/// <param name="Label">
/// Display name for THIS ability, when its rule offers more than one to choose between (#197 P5a:
/// "when this unit is activated, pick one effect: ... either get AP(+1) or get +1 to hit rolls"). Such a
/// rule carries one <see cref="ActivatedAbility"/> per effect, all at the same <paramref name="TriggerHook"/>,
/// and the label is what the player picks by — <c>AbilityOffer.RuleName</c> can't distinguish them.
/// Empty (the default, and the case for every single-ability rule) means "display the rule's name".
///
/// A once-per-X <see cref="Cost"/> is keyed on the RULE name, not the ability, so choosing one effect
/// spends the gate for all of them. That is exactly the "pick one" semantics the corpus wants.
/// </param>
public record ActivatedAbility(EHookID TriggerHook, Cost Cost, TargetSelector TargetSelector,
    Effect Effect, Condition AvailableWhen, string Label = "");
