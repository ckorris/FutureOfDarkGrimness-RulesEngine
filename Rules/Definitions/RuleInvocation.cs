using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// The resolution environment for one firing of one rule instance: the world event
/// (<see cref="Hook"/> — null when resolving an accepted activated ability, which
/// happens off the live-event path), who the rule is attached to (<see cref="Bearer"/>),
/// that attachment's arguments (<see cref="Arguments"/>, e.g. Deadly(3)), the entity
/// the effect lands on (<see cref="Target"/> — null for passive rules, which act on the
/// bearer), and a <see cref="DiceRoller"/> for effects with random amounts (Mend's D3).
/// Bearer/arguments/target/roller can't live on the hook context because the same event
/// is evaluated for several units/seats — they vary per firing, which is what builds the
/// invocation. <see cref="Weapon"/> is the carrying weapon when the rule is
/// weapon-attached (#027) — null for unit-attached rules. <see cref="Definition"/> is
/// the rule definition being fired, for the rare effect that must reason about its own
/// rule's identity (Ravage and Brace counting "models with this rule"). <see cref="Attachment"/> is
/// the exact attachment firing - the evaluator's dedup unit - for an effect that must count the models
/// carrying THIS copy of an (X) rule rather than every copy (#422 Brace: one shared attachment on a
/// fresh army's weapons, one per weapon after a resume rehydrates them). Null off the passive
/// dispatch path (activated abilities, hand-built invocations).
/// </summary>
// #434: TargetCount - how many units the activated ability was resolved against (1 on the passive path), so a value can
// depend on the spread: 3.6 Piercing / Precision Tag "place X tokens on one enemy unit, or one token on X enemy units"
// (ValueSource.ArgIfSoleTarget).
public sealed record RuleInvocation(
    IHookContext? Hook,
    IUnit Bearer,
    IReadOnlyList<RuleArgument> Arguments,
    IUnit? Target = null,
    IDiceRoller? DiceRoller = null,
    IWeapon? Weapon = null,
    SpecialRuleDefinition? Definition = null,
    Dispatch.ResolvedRule? Attachment = null,
    int TargetCount = 1)
{
    /// <summary>
    /// The unit an effect lands on: the explicit <see cref="Target"/> for activated
    /// abilities, or the <see cref="Bearer"/> itself for passive rules.
    /// </summary>
    public IUnit EffectiveTarget => Target ?? Bearer;

    /// <summary>
    /// The owner to stamp on a token this firing grants: the bearer when the token lands
    /// on another unit (cross-unit provenance — drives OwnerDestroyed cleanup and
    /// per-placer stacking), null when the token lands on the bearer itself.
    /// </summary>
    public UnitID? OwnerForEffectiveTarget =>
        EffectiveTarget.ID != Bearer.ID ? Bearer.ID : null;
}
