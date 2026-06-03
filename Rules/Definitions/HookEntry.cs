using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// One passive wiring of a rule: at the given <see cref="HookID"/>, if the
/// <see cref="Condition"/> matches the live hook context, the
/// <see cref="Effect"/> fires with the given <see cref="Lifetime"/> scope.
///
/// The atomic unit of "passive rule attachment." A <see cref="SpecialRuleDefinition"/>
/// typically carries several HookEntries — one per hook the rule attaches to.
/// E.g. Furious has a single HookEntry: <c>(OnHitRollComplete,
/// UnmodifiedRollEquals(6), AddExtraHit, ThisAttack)</c>. A rule that affects
/// both movement and combat would have one HookEntry per concern.
///
/// Distinct from <see cref="ActivatedAbility"/>, which represents
/// player-triggered (cost-gated, target-picking) abilities rather than passive
/// fire-on-hook wiring.
///
/// <see cref="Seat"/> is the perspective half of the "when": the role the bearer
/// must be playing in the firing context for this entry to fire. Defaults to
/// <see cref="ERuleSeat.Actor"/> (the acting side), so only defensive entries
/// (Stealth, Bane, ...) need to state <see cref="ERuleSeat.Subject"/> explicitly.
/// </summary>
public record HookEntry(EHookID HookID, Condition Condition, Effect Effect, ELifetime Lifetime,
    ERuleSeat Seat = ERuleSeat.Actor);
