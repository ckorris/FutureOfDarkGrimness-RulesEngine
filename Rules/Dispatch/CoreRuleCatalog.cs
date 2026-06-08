using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Single source of truth for the core special-rule definitions that #042 has
/// implemented end-to-end (live in real stages). Army-load resolves army-list
/// rule names against a <see cref="RuleResolver"/> built from this catalog via
/// <see cref="CreateResolver"/>; the integration test suite asserts against these
/// same definitions, so the tests validate the catalog.
///
/// As more core rules go live, add their definition here. Army-specific (non-core)
/// rules are out of scope — those land with the future JSON loader.
/// </summary>
public static class CoreRuleCatalog
{
    /// <summary>
    /// Every implemented core rule, in registration order. Computed on access (not a
    /// static field) so it doesn't depend on static-initializer ordering relative to
    /// the per-rule properties below.
    /// </summary>
    public static IReadOnlyList<SpecialRuleDefinition> All => new[]
    {
        Stealth, Artillery, Indirect, Reliable, Fast, VeryFast, Slow, Surge, Relentless, Furious,
        Deadly, Regeneration, Unstoppable, Tough, Rending,
    };

    /// <summary>
    /// A fresh <see cref="RuleResolver"/> with every catalog rule registered under
    /// its canonical name. Built once at army-load.
    /// </summary>
    public static RuleResolver CreateResolver()
    {
        RuleResolver resolver = new RuleResolver();
        foreach (SpecialRuleDefinition definition in All)
        {
            resolver.Register(definition);
        }
        return resolver;
    }

    // Hit-roll-modifier sink (DetermineHitRollNeededStage) -----------------------

    /// <summary> Defensive: enemies shooting this unit from &gt;9" take -1 to hit. </summary>
    public static SpecialRuleDefinition Stealth { get; } = new SpecialRuleDefinition("Stealth",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.DistanceGreaterThan(9f),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Attacker: +1 to hit beyond 9". </summary>
    public static SpecialRuleDefinition Artillery { get; } = new SpecialRuleDefinition("Artillery",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.DistanceGreaterThan(9f),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Attacker: -1 to hit when the unit moved this activation. </summary>
    public static SpecialRuleDefinition Indirect { get; } = new SpecialRuleDefinition("Indirect",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.AfterMoving(),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Attacker: base Quality is treated as 2+ (still modifiable). </summary>
    public static SpecialRuleDefinition Reliable { get; } = new SpecialRuleDefinition("Reliable",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Always(),
                new Effect.QualityFloor(Quality: 2),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    // Movement-modifier sink (MovementActionContext) -----------------------------

    /// <summary> Advance +2". </summary>
    public static SpecialRuleDefinition Fast { get; } = new SpecialRuleDefinition("Fast",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Fast, doubled: Advance +4". </summary>
    public static SpecialRuleDefinition VeryFast { get; } = new SpecialRuleDefinition("Very Fast",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Advance -2", Rush/Charge -4". </summary>
    public static SpecialRuleDefinition Slow { get; } = new SpecialRuleDefinition("Slow",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: -2f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Rush),
                new Effect.MovementBonus(EActionType.Rush, DistanceInches: -4f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: -4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>());

    // Hit-injection sink (RollToHitStage) ----------------------------------------

    /// <summary> Extra hit on an unmodified 6 (shooting and melee). </summary>
    public static SpecialRuleDefinition Surge { get; } = new SpecialRuleDefinition("Surge",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.UnmodifiedRollEquals(6),
                new Effect.AddExtraHit(OnRollValue: 6),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Extra hit on an unmodified 6 when beyond 9". </summary>
    public static SpecialRuleDefinition Relentless { get; } = new SpecialRuleDefinition("Relentless",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.UnmodifiedRollEquals(6),
                                  new Condition.DistanceGreaterThan(9f)),
                new Effect.AddExtraHit(OnRollValue: 6),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Extra hit on an unmodified 6 in melee. </summary>
    public static SpecialRuleDefinition Furious { get; } = new SpecialRuleDefinition("Furious",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.IsMelee(),
                                  new Condition.UnmodifiedRollEquals(6)),
                new Effect.AddExtraHit(OnRollValue: 6),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    // Wound-modifier sink (AssignWoundsStage) ------------------------------------

    /// <summary> Deadly(X): the attack's wounds are multiplied by X (the rule's argument). </summary>
    public static SpecialRuleDefinition Deadly { get; } = new SpecialRuleDefinition("Deadly",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnPreApplyWound,
                new Condition.Always(),
                new Effect.MultiplyWounds(new ValueSource.Arg(0)),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    // Wound-ignore sink (AssignWoundsStage) --------------------------------------

    /// <summary> Defensive: the unit ignores each wound on a roll of 5+. </summary>
    public static SpecialRuleDefinition Regeneration { get; } = new SpecialRuleDefinition("Regeneration",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreWoundOnRoll(MinRoll: 5),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary>
    /// Attacker: ignores the target's Regeneration (the suppression facet). The rulebook's second
    /// facet — "ignores all negative modifiers to this weapon" — needs a modifier-immunity effect
    /// and lands when that's modelled.
    /// </summary>
    public static SpecialRuleDefinition Unstoppable { get; } = new SpecialRuleDefinition("Unstoppable",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreRule("Regeneration"),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary>
    /// Attacker: an unmodified 6 to hit promotes the attack's AP (modelled as -4 to the defender's
    /// save), and the attack ignores Regeneration. The AP facet is folded at hit-roll-complete and
    /// carried to the save stage; the suppress facet rides the save-complete evaluation. Simplified:
    /// the save modifier applies to the whole attack when ANY hit rolled a 6 (true per-hit AP deferred).
    /// </summary>
    public static SpecialRuleDefinition Rending { get; } = new SpecialRuleDefinition("Rending",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.UnmodifiedRollEquals(6),
                new Effect.RollModifier(ERollKind.Save, Delta: -4),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreRule("Regeneration"),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

    // Max-wounds sink (UnitCreationRules, at army-load) ---------------------------

    /// <summary> Tough(X): each model in the unit has X wounds instead of 1. </summary>
    public static SpecialRuleDefinition Tough { get; } = new SpecialRuleDefinition("Tough",
        new[]
        {
            new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                new Condition.Always(),
                new Effect.SetMaxWounds(new ValueSource.Arg(0)),
                ELifetime.UntilEndOfGame),
        },
        Array.Empty<ActivatedAbility>());
}
