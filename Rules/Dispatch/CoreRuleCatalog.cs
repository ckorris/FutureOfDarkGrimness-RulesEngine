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
        Deadly, Regeneration, Unstoppable, Tough, Rending, Bane, Shred, Vanguard, Scout, Ambush, Thrust,
        Blast, Takedown, Limited, Impact, Counter, MartialProwess, Strafing, Fear, Fearless, Hero, Transport,
        FuriousBuff, Mend, Immobile, Caster,
        Evasive, MeleeEvasion, Precise, GoodShot,
        Agile, Quick, RapidAdvance, RapidRush, RapidCharge,
        Lacerate, Crack, CounterAttack,
        UnstoppableWhenShooting, ShredWhenShooting, BaneWhenShooting,
        Harassing, HitAndRunShooter, HitAndRunFighter, HitAndRun, Guerrilla,
        HarassingBoost, GuerrillaBoost,
        HitAndRunShooterAura, HitAndRunFighterAura, HarassingBoostAura, GuerrillaBoostAura,
        RegenerationAura, FuriousAura, StealthAura, ScoutAura, RelentlessAura, AmbushAura,
        CounterAttackAura, EvasiveAura, FastAura, FearlessAura, MeleeEvasionAura,
        RapidAdvanceAura, RapidChargeAura, RapidRushAura,
        BaneWhenShootingAura, ShredWhenShootingAura, UnstoppableWhenShootingAura,
        Courage, UnstoppableInMelee, ShredInMelee, BaneInMelee, RendingInMelee,
        CourageAura, BaneInMeleeAura, RendingInMeleeAura, ShredInMeleeAura, UnstoppableInMeleeAura,
        Resistance, Protected, PiercingAssault, PiercingHunter, Shielded, Fortified,
        ResistanceAura, ProtectedAura, PiercingAssaultAura, PiercingHunterAura, ShieldedAura, FortifiedAura,
        Strider, Flying, Aircraft, Teleport, TeleportAura, DelayedAction,
        IncreasedShootingRange, RangedShrouding, DarkbornOffensive, DarkbornDefensive, MeleeShrouding,
        IncreasedShootingRangeAura, RangedShroudingAura, MeleeShroudingAura,
        Unpredictable, UnpredictableFighter, UnpredictableShooter,
        UnpredictableFighterAura, UnpredictableShooterAura,
        Ravage, CrossingAttack,
        StormOfChange, StormOfLust, StormOfPlague, StormOfWar,
        Fanatic, ReDeployment,
        Retaliate, Deathstrike, SelfDestruct,
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

    /// <summary>
    /// Factory for the corpus's uniform "<c>X Aura</c>: this model and its unit get X" rules — an
    /// <see cref="Effect.Aura"/> at <see cref="EHookID.Lifecycle_OnUnitCreated"/> that grants an
    /// already-cataloged rule unit-wide (read back by <c>RuleEvaluator.CollectGrantedRules</c>).
    /// <paramref name="grantedRuleName"/> must EXACTLY match a registered rule name — the resolver is
    /// case-sensitive, so e.g. the base is "Bane when shooting" even though the aura is "Bane when Shooting
    /// Aura". Every aura in <see cref="All"/> is checked against the resolver by a catalog-integrity test.
    /// </summary>
    private static SpecialRuleDefinition UnitAura(string name, string grantedRuleName) =>
        new SpecialRuleDefinition(name,
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                    new Condition.Always(),
                    new Effect.Aura(grantedRuleName),
                    ELifetime.UntilEndOfGame),
            },
            Array.Empty<ActivatedAbility>(),
            Description: $"This model and its whole unit gain {grantedRuleName}.");

    // Hit-roll-modifier sink (DetermineHitRollStage) -----------------------

    /// <summary> Defensive: enemies shooting this unit from &gt;9" take -1 to hit. </summary>
    public static SpecialRuleDefinition Stealth { get; } = new SpecialRuleDefinition("Stealth",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.DistanceGreaterThan(9f), new Condition.AllModelsHaveThisRule()),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Enemies shooting this unit from over 9\" away take -1 to hit.");

    /// <summary>
    /// Artillery: as an attacker, +1 to hit beyond 9" (Actor); as a target, enemies shooting it from
    /// beyond 9" take -2 to hit (Subject). Both ride the existing hit-roll-modifier sink at
    /// <see cref="EHookID.Shooting_OnHitRollModifier"/>; the <c>DistanceGreaterThan(9)</c> condition
    /// naturally excludes melee (melee is within 2"), so neither needs a combat-kind gate. The
    /// "Hold-only action restriction" facet (W5) is still deferred — see Appendix C.
    /// </summary>
    public static SpecialRuleDefinition Artillery { get; } = new SpecialRuleDefinition("Artillery",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.DistanceGreaterThan(9f),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.DistanceGreaterThan(9f), new Condition.AllModelsHaveThisRule()),
                new Effect.RollModifier(ERollKind.Hit, Delta: -2),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Beyond 9\", this unit gets +1 to hit when shooting, and enemies shooting it take -2 to hit.");

    /// <summary>
    /// Indirect: -1 to hit when the unit moved this activation (shooting only — the AfterMoving gate is
    /// joined with Not(IsMelee) so a charge, which sets HasMoved, doesn't wrongly penalise melee swings),
    /// and the weapon may fire at targets out of line of sight as if in LoS, ignoring cover. The LoS- and
    /// cover-ignore facets queue <see cref="RuleOperation.IgnoreLineOfSight"/> /
    /// <see cref="RuleOperation.IgnoreCover"/> at <see cref="EHookID.Shooting_OnSaveRollModifier"/>, read
    /// by the targeting/occlusion/cover stages (and surfaced to the resolvers) via
    /// <see cref="Rules.Dispatch.SightRuleQueries"/>.
    /// </summary>
    public static SpecialRuleDefinition Indirect { get; } = new SpecialRuleDefinition("Indirect",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.AfterMoving(), new Condition.Not(new Condition.IsMelee())),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnSaveRollModifier,
                new Condition.Always(),
                new Effect.IgnoreLineOfSight(),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnSaveRollModifier,
                new Condition.Always(),
                new Effect.IgnoreCover(),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "Fires at targets out of line of sight and ignores cover; -1 to hit if the unit moved this activation.");

    /// <summary> Weapon rule ("models attacks at Quality 2+ with this weapon"): base Quality is treated as 2+ (still modifiable). </summary>
    public static SpecialRuleDefinition Reliable { get; } = new SpecialRuleDefinition("Reliable",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Always(),
                new Effect.QualityFloor(Quality: 2),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "Attacks with this weapon hit on a Quality of 2+ (before other modifiers).");

    /// <summary>
    /// Defensive: enemies take -1 to hit when attacking this unit, in melee or shooting. The Subject-seat
    /// mirror of <see cref="Precise"/>; no distance/combat-kind gate, so it raises the threshold on every
    /// attack against the bearer (the same <c>OnHitRollModifier</c> sink Stealth/Artillery use).
    /// </summary>
    public static SpecialRuleDefinition Evasive { get; } = new SpecialRuleDefinition("Evasive",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.AllModelsHaveThisRule(),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Enemies take -1 to hit when attacking this unit, in melee or shooting.");

    /// <summary>
    /// Defensive: enemies take -1 to hit in melee when attacking this unit. As <see cref="Evasive"/> but
    /// gated to melee via <c>IsMelee</c> (the same combat-kind condition Thrust/Indirect read), so the
    /// penalty applies only to melee swings against the bearer, not to shooting.
    /// </summary>
    public static SpecialRuleDefinition MeleeEvasion { get; } = new SpecialRuleDefinition("Melee Evasion",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.IsMelee(), new Condition.AllModelsHaveThisRule()),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Enemies take -1 to hit when attacking this unit in melee.");

    /// <summary>
    /// Attacker: +1 to hit when attacking with this weapon (melee or shooting) — lowers the bearer's hit
    /// threshold. The Actor-seat counterpart to <see cref="Evasive"/>, no gate.
    ///
    /// Weapon-scoped (#197 slice 0): every corpus reference attaches it to a weapon, either directly on a
    /// profile or through a targeted wargear upgrade ("upgrade the Marksman Carbine with a Scope"), which
    /// ListCompiler lands on the named weapon. A model carrying a scoped carbine and a plain sidearm must
    /// get the bonus only on the carbine, so the bearer has to be the weapon rather than the unit.
    /// </summary>
    public static SpecialRuleDefinition Precise { get; } = new SpecialRuleDefinition("Precise",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Always(),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "+1 to hit when attacking with this weapon, in melee or shooting.");

    /// <summary>
    /// Attacker: +1 to hit when shooting only — gated to non-melee via <c>Not(IsMelee)</c> (the gate
    /// Indirect uses), so the bonus rides shooting attacks but not melee swings.
    /// </summary>
    public static SpecialRuleDefinition GoodShot { get; } = new SpecialRuleDefinition("Good Shot",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Not(new Condition.IsMelee()),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "+1 to hit when this unit shoots.");

    // Movement-modifier sink (MovementActionContext) -----------------------------

    /// <summary> Advance +2", Rush/Charge +4" (the positive mirror of <see cref="Slow"/>). </summary>
    public static SpecialRuleDefinition Fast { get; } = new SpecialRuleDefinition("Fast",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Rush),
                new Effect.MovementBonus(EActionType.Rush, DistanceInches: 4f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Moves +2\" when Advancing and +4\" when Rushing or Charging.");

    /// <summary> Fast, doubled: Advance +4", Rush/Charge +8". </summary>
    public static SpecialRuleDefinition VeryFast { get; } = new SpecialRuleDefinition("Very Fast",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 4f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Rush),
                new Effect.MovementBonus(EActionType.Rush, DistanceInches: 8f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 8f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Moves +4\" when Advancing and +8\" when Rushing or Charging.");

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
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Moves -2\" when Advancing and -4\" when Rushing or Charging.");

    /// <summary> Advance +1", Rush/Charge +2" (a smaller Fast). </summary>
    public static SpecialRuleDefinition Agile { get; } = new SpecialRuleDefinition("Agile",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 1f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Rush),
                new Effect.MovementBonus(EActionType.Rush, DistanceInches: 2f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 2f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Moves +1\" when Advancing and +2\" when Rushing or Charging.");

    /// <summary> Advance +2", Rush/Charge +2". </summary>
    public static SpecialRuleDefinition Quick { get; } = new SpecialRuleDefinition("Quick",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Rush),
                new Effect.MovementBonus(EActionType.Rush, DistanceInches: 2f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 2f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Moves +2\" when Advancing, Rushing, or Charging.");

    /// <summary> Advance +4" (action-specific rapid mover). </summary>
    public static SpecialRuleDefinition RapidAdvance { get; } = new SpecialRuleDefinition("Rapid Advance",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Moves +4\" when Advancing.");

    /// <summary> Rush +6". </summary>
    public static SpecialRuleDefinition RapidRush { get; } = new SpecialRuleDefinition("Rapid Rush",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Rush),
                new Effect.MovementBonus(EActionType.Rush, DistanceInches: 6f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Moves +6\" when Rushing.");

    /// <summary> Charge +4". </summary>
    public static SpecialRuleDefinition RapidCharge { get; } = new SpecialRuleDefinition("Rapid Charge",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Moves +4\" when Charging.");

    // Hit-injection sink (RollToHitStage) ----------------------------------------

    /// <summary> Weapon rule ("this weapon deals 1 extra hit"): extra hit on an unmodified 6 (shooting and melee). </summary>
    public static SpecialRuleDefinition Surge { get; } = new SpecialRuleDefinition("Surge",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.UnmodifiedRollEquals(6),
                new Effect.AddExtraHit(OnRollValue: 6),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "Each unmodified 6 to hit scores one extra hit.");

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
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Beyond 9\", each unmodified 6 to hit scores an extra hit.");

    /// <summary> Extra hit on an unmodified 6 in melee, but only on the charge (not a strike-back). </summary>
    public static SpecialRuleDefinition Furious { get; } = new SpecialRuleDefinition("Furious",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.IsMelee(),
                                  new Condition.And(new Condition.IsCharging(),
                                                    new Condition.UnmodifiedRollEquals(6))),
                new Effect.AddExtraHit(OnRollValue: 6),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "On a charge, each unmodified 6 to hit in melee scores an extra hit.");

    // Hit-multiplier sink (RollToHitStage, after injection) ----------------------

    /// <summary>
    /// Blast(X): EACH hit is multiplied by X (the rule's argument), with X capped per hit at the target
    /// unit's living model count, and the attack ignores the target's cover. The cap bounds one hit's
    /// fan-out, not the volley's total, so the multiplied hits stack: an A3 Blast(3) landing 3 hits deals
    /// 9 against a 3-model unit and 6 against a 2-model one (owner-ruled 2026-07-31). The multiply folds
    /// at <see cref="EHookID.Shooting_OnHitRollComplete"/> AFTER the hit-injection rules ("after other
    /// rules"); the cover-ignore fires at <see cref="EHookID.Shooting_OnSaveRollModifier"/>, where the
    /// cover stage drops the bonus and the targeting/movement option builders flag the weapon.
    /// </summary>
    public static SpecialRuleDefinition Blast { get; } = new SpecialRuleDefinition("Blast",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.Always(),
                new Effect.MultiplyHits(new ValueSource.Arg(0)),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnSaveRollModifier,
                new Condition.Always(),
                new Effect.IgnoreCover(),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "Each hit becomes several hits (no more per hit than the target unit's model count) " +
                     "and the attack ignores cover.");

    // Targets-selected marker (BuildTargetListStage, shooting) -------------------

    /// <summary>
    /// Takedown: the shooter may pick a single model in the target unit and resolve the attack against
    /// just it ("a unit of [1]") — all wounds funnel to that model with no carry-over. A passive rule at
    /// <see cref="EHookID.Shooting_OnShootTargetsSelected"/> that queues
    /// <see cref="RuleOperation.TargetIndividualModel"/>; BuildTargetListStage reads it, asks the
    /// attacker to pick the model, and AssignWoundsStage confines the wounds.
    ///
    /// The rule does NOT ignore line of sight or cover (#314). It carried
    /// <see cref="RuleOperation.IgnoreLineOfSight"/> / <see cref="RuleOperation.IgnoreCover"/> hooks from
    /// 2026-06-11 to 2026-08-02, copied off Indirect's clause by the #042 checklist's per-rule mapping row;
    /// the v3.5.1 rule text grants neither, and the LoS hook let snipers shoot through Blocking terrain.
    /// Indirect keeps both — its own text does grant them.
    ///
    /// "Takedown attacks must be resolved before other weapons" is the ordering facet, realized as a
    /// picker gate rather than a hook: <c>WoundPriorityQueries.ShootingResolveFirstSource</c> reads the
    /// queued <see cref="RuleOperation.TargetIndividualModel"/> and ChooseRangedAttackStage marks the
    /// bearer's other weapons unselectable while a Takedown weapon still has a target (the same gate
    /// Deadly's "resolved first" uses).
    /// </summary>
    public static SpecialRuleDefinition Takedown { get; } = new SpecialRuleDefinition("Takedown",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnShootTargetsSelected,
                new Condition.Always(),
                new Effect.TargetIndividualModel(),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "May fire at a single chosen model in the target unit; all wounds hit that model. " +
                     "Must be resolved before the unit's other weapons.");

    /// <summary>
    /// Limited (#032): "may only be used once per game." A weapon-scoped MARKER rule — no hooks; the behaviour
    /// lives in engine gates that read it via <see cref="LimitedRules"/> (the Aircraft/Transport marker shape),
    /// because there's no per-weapon-fired rule context to hang an effect on. The spent-state is a per-MODEL
    /// <see cref="Foundation.TokenType.LimitedSpent"/> token (weapons have no token container, models do), so a
    /// unit's copies — which by the rules all fire together — each record their own fired-state and casualties
    /// drop it with the model. The shooting flow excludes a Limited weapon once every living model carrying it
    /// has fired it (<c>ChooseRangedAttackStage</c>).
    /// </summary>
    public static SpecialRuleDefinition Limited { get; } = new SpecialRuleDefinition("Limited",
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Neutral,
        Description: "This weapon may only be used once per game.");

    // Melee charge rules on the shared hit + save sinks --------------------------

    /// <summary>
    /// Thrust: when charging in melee, +1 to hit and AP(+1). Both ride existing sinks — the +1
    /// to hit folds through the hit-roll-modifier sink at <see cref="EHookID.Shooting_OnHitRollModifier"/>
    /// (shared shoot/melee hook), and AP(+1) is modelled as a -1 save modifier folded at
    /// <see cref="EHookID.Shooting_OnHitRollComplete"/> and carried to the save stage (same machinery
    /// as Rending). The melee + charging gate distinguishes the charger's swing from a strike-back.
    ///
    /// Weapon-scoped (#197 slice 0), matching where the corpus attaches it. The melee gate means a
    /// unit-scoped attachment would have behaved identically for a single-melee-weapon model, but a
    /// charger holding two melee weapons must only get the bonus on the one carrying the rule.
    /// </summary>
    public static SpecialRuleDefinition Thrust { get; } = new SpecialRuleDefinition("Thrust",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.IsMelee(), new Condition.IsCharging()),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.IsMelee(), new Condition.IsCharging()),
                new Effect.RollModifier(ERollKind.Save, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "When charging, +1 to hit and AP(+1) with this weapon in melee.");

    /// <summary>
    /// Impact(X): on charge contact, the charger rolls X dice — each 2+ scores an automatic hit on the
    /// defender, resolved through save + wound BEFORE the normal melee swings. A passive rule at
    /// <see cref="EHookID.Melee_OnChargeContact"/> that queues <see cref="RuleOperation.ChargeImpactHits"/>;
    /// ResolveImpactHitsStage rolls the dice and runs the hits through the save→wound sub-pipeline.
    /// The "not fatigued" gate is deferred (fatigue is absent) — see Appendix C.
    /// </summary>
    public static SpecialRuleDefinition Impact { get; } = new SpecialRuleDefinition("Impact",
        new[]
        {
            new HookEntry(EHookID.Melee_OnChargeContact,
                new Condition.Always(),
                new Effect.ChargeImpactHits(new ValueSource.Arg(0)),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "On charging into contact, rolls bonus dice that score automatic hits before melee swings.");

    /// <summary>
    /// Ravage(X): when this unit attacks in melee, each model carrying the rule rolls X dice — every 6+
    /// deals one DIRECT wound to the defender (no armor save, but Regeneration and Tough still apply),
    /// resolved BEFORE the normal swings. A passive at <see cref="EHookID.Melee_OnChargeContact"/> (melee is
    /// only ever entered via Charge) that queues <see cref="RuleOperation.InvokeDealAutoWounds"/>;
    /// <c>ResolveRavageWoundsStage</c> rolls the pool and feeds the wounds straight into wound assignment,
    /// skipping the save roll. The per-model scaling (X x living carriers) is folded in
    /// <see cref="Effect.DealAutoWounds"/>, mirroring Impact's dice-count handling.
    /// </summary>
    public static SpecialRuleDefinition Ravage { get; } = new SpecialRuleDefinition("Ravage",
        new[]
        {
            new HookEntry(EHookID.Melee_OnChargeContact,
                new Condition.Always(),
                new Effect.DealAutoWounds(new ValueSource.Arg(0), SuccessThreshold: 6),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When attacking in melee, rolls dice that deal automatic unsaveable wounds before swings.");

    /// <summary>
    /// Counter: when this unit is charged, it strikes FIRST — before the charging unit's strikes — and
    /// the charger rolls one fewer Impact die per living model of this unit. Both are defensive (Subject
    /// seat). StrikeFirst fires at <see cref="EHookID.Melee_OnCounterTrigger"/>; DetermineStrikeOrderStage
    /// swaps the attacker/defender roles so the existing swing/strike-back flow resolves the Counter unit
    /// first. The impact reduction fires at <see cref="EHookID.Melee_OnChargeContact"/> (the same when the
    /// charger's Impact(X) fires), emitting a negative <see cref="RuleOperation.ChargeImpactHits"/> that
    /// folds into the shared impact-dice sink, so ResolveImpactHitsStage rolls the net count.
    /// </summary>
    public static SpecialRuleDefinition Counter { get; } = new SpecialRuleDefinition("Counter",
        new[]
        {
            new HookEntry(EHookID.Melee_OnCounterTrigger,
                new Condition.Always(),
                new Effect.StrikeFirst(),
                ELifetime.ThisActivation,
                ERuleSeat.Subject),
            new HookEntry(EHookID.Melee_OnChargeContact,
                new Condition.Always(),
                new Effect.ReduceImpactDicePerModel(),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "When charged, this unit strikes first and the charger rolls one fewer Impact die per living model.");

    /// <summary>
    /// Counter-Attack: this unit strikes first when charged — Counter's strikes-first facet alone, without
    /// the impact-dice reduction. Fires <see cref="Effect.StrikeFirst"/> at
    /// <see cref="EHookID.Melee_OnCounterTrigger"/> on the Subject (the charged unit), so
    /// DetermineStrikeOrderStage swaps it to swing first. Unit-scoped (no per-weapon facet).
    /// </summary>
    public static SpecialRuleDefinition CounterAttack { get; } = new SpecialRuleDefinition("Counter-Attack",
        new[]
        {
            new HookEntry(EHookID.Melee_OnCounterTrigger,
                new Condition.AllModelsHaveThisRule(),
                new Effect.StrikeFirst(),
                ELifetime.ThisActivation,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When charged, this unit strikes first.");

    // Melee-resolution (DetermineMeleeWinnerStage) -------------------------------

    /// <summary>
    /// Fear(X): this unit counts as dealing +X extra wounds when deciding who won a melee — it does NOT
    /// deal real wounds. A passive rule at <see cref="EHookID.Melee_OnMeleeResolution"/> that emits
    /// <see cref="RuleOperation.ExtraMeleeWoundCount"/> (X from the rule's argument); DetermineMeleeWinnerStage
    /// folds each side's total into its wounds-dealt before the winner comparison, so the loser — and thus
    /// which unit must test morale — can flip.
    /// </summary>
    public static SpecialRuleDefinition Fear { get; } = new SpecialRuleDefinition("Fear",
        new[]
        {
            new HookEntry(EHookID.Melee_OnMeleeResolution,
                new Condition.Always(),
                new Effect.ExtraMeleeWoundCount(new ValueSource.Arg(0)),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Counts as dealing extra wounds when deciding who won a melee (does not deal real wounds).");

    // Morale (RollForMoraleStage) ------------------------------------------------

    /// <summary>
    /// Fearless: when this unit fails a morale test, it rolls a fresh die and passes on a 4+ regardless
    /// of Quality. A passive rule at <see cref="EHookID.Morale_OnMoraleTestComplete"/> that requests a
    /// morale re-roll on all failures; RollForMoraleStage executes the second chance with the rulebook's
    /// fixed 4+ threshold. (Modifier-style morale rules — e.g. a future Courage — ride the separate
    /// <see cref="EHookID.Morale_OnPreMoraleTest"/> hook instead.)
    /// </summary>
    public static SpecialRuleDefinition Fearless { get; } = new SpecialRuleDefinition("Fearless",
        new[]
        {
            new HookEntry(EHookID.Morale_OnMoraleTestComplete,
                new Condition.AllModelsHaveThisRule(),
                new Effect.Reroll(ERollKind.Morale, new RerollCondition.AllFailures()),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When this unit fails a morale test, it re-rolls and passes on a 4+.");

    /// <summary>
    /// Courage: +1 to this unit's morale test rolls. The modifier-style morale rule the Fearless doc
    /// above anticipates — a passive <see cref="HookEntry"/> at <see cref="EHookID.Morale_OnPreMoraleTest"/>
    /// whose <see cref="Effect.RollModifier"/>(Morale, +1) MoraleUtilities.TakeMoraleTest folds into the
    /// threshold (a +N lowers the roll needed, so the test is easier). The base rule behind Courage Aura /
    /// Courage Buff (the corpus names only the aura/buff, which inline "+1 to morale test rolls").
    /// </summary>
    public static SpecialRuleDefinition Courage { get; } = new SpecialRuleDefinition("Courage",
        new[]
        {
            new HookEntry(EHookID.Morale_OnPreMoraleTest,
                new Condition.Always(),
                new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "+1 to this unit's morale test rolls.");

    // Wound-modifier sink (AssignWoundsStage) ------------------------------------

    /// <summary> Deadly(X), weapon rule: the attack's wounds are multiplied by X (the rule's argument). </summary>
    public static SpecialRuleDefinition Deadly { get; } = new SpecialRuleDefinition("Deadly",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnPreApplyWound,
                new Condition.Always(),
                new Effect.MultiplyWounds(new ValueSource.Arg(0)),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "Each wound from this weapon is multiplied; excess wounds don't carry over to other models.");

    // Wound-ignore sink (AssignWoundsStage) --------------------------------------

    /// <summary> Defensive: the unit ignores each wound on a roll of 5+. </summary>
    public static SpecialRuleDefinition Regeneration { get; } = new SpecialRuleDefinition("Regeneration",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.AllModelsHaveThisRule(),
                new Effect.IgnoreWoundOnRoll(MinRoll: 5),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Ignores each wound on a roll of 5+.");

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
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "This weapon's attacks ignore the target's Regeneration.");

    /// <summary>
    /// Attacker: each hit that rolled an unmodified 6 promotes to AP(4) — modelled as -4 to the
    /// defender's save on those hits only — and the attack ignores Regeneration. The per-hit AP facet
    /// fires at hit-roll-complete; RollToHitStage peels the natural-6 hits into their own save group
    /// (the rest save at base AP), and the Regeneration-ignore facet rides the save-complete evaluation.
    /// </summary>
    public static SpecialRuleDefinition Rending { get; } = new SpecialRuleDefinition("Rending",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.UnmodifiedRollEquals(6),
                new Effect.PerHitSaveModifier(OnRollValue: 6, Delta: -4),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreRule("Regeneration"),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "An unmodified 6 to hit gives the attack AP(+4), and the attack ignores Regeneration.");

    /// <summary>
    /// Crack: each hit that rolled an unmodified 6 gives that hit AP(+2) — modelled as -2 to the
    /// defender's save on those hits only — by the same per-hit machinery as <see cref="Rending"/>, at
    /// half the AP and without Rending's Regeneration-ignore.
    /// </summary>
    public static SpecialRuleDefinition Crack { get; } = new SpecialRuleDefinition("Crack",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.UnmodifiedRollEquals(6),
                new Effect.PerHitSaveModifier(OnRollValue: 6, Delta: -2),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "An unmodified 6 to hit gives the attack AP(+2).");

    /// <summary>
    /// Attacker: the defender must re-roll unmodified Defense 6s (turning saved 6s into possible
    /// failures), and the attack ignores Regeneration. Both facets ride the save-complete evaluation.
    /// </summary>
    public static SpecialRuleDefinition Bane { get; } = new SpecialRuleDefinition("Bane",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue()),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreRule("Regeneration"),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "The defender must re-roll unmodified Defense 6s, and the attack ignores Regeneration.");

    /// <summary>
    /// Lacerate: when attacking, the defender must re-roll unmodified Defense 6s — <see cref="Bane"/>'s
    /// save-reroll facet without the Regeneration-ignore. Rides the save-complete evaluation.
    /// </summary>
    public static SpecialRuleDefinition Lacerate { get; } = new SpecialRuleDefinition("Lacerate",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue()),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "The defender must re-roll unmodified Defense 6s.");

    // Combat-kind-scoped variants (#093) -----------------------------------------
    // A base save-side rule gated to shooting via Not(IsMelee), now that IsMelee is threaded into
    // SaveRollCompleteContext. These are the named rules the "X when shooting" spell grants (and units)
    // resolve to. The "in melee" mirror is the same with Condition.IsMelee().

    /// <summary> Unstoppable on shooting attacks only: ignores Regeneration when shooting (Not melee). </summary>
    public static SpecialRuleDefinition UnstoppableWhenShooting { get; } =
        new SpecialRuleDefinition("Unstoppable when shooting",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.Not(new Condition.IsMelee()),
                    new Effect.IgnoreRule("Regeneration"),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "When shooting, this weapon's attacks ignore the target's Regeneration.");

    /// <summary> Shred on shooting attacks only: +1 wound per unmodified save of 1 when shooting. </summary>
    public static SpecialRuleDefinition ShredWhenShooting { get; } =
        new SpecialRuleDefinition("Shred when shooting",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.Not(new Condition.IsMelee()),
                    new Effect.AddExtraWound(OnRollValue: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "When shooting, each of the defender's unmodified Defense rolls of 1 deals an extra wound.");

    /// <summary> Bane on shooting attacks only: defender re-rolls unmodified Defense 6s + ignore Regeneration, when shooting. </summary>
    public static SpecialRuleDefinition BaneWhenShooting { get; } =
        new SpecialRuleDefinition("Bane when shooting",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.Not(new Condition.IsMelee()),
                    new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue()),
                    ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.Not(new Condition.IsMelee()),
                    new Effect.IgnoreRule("Regeneration"),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "When shooting, the defender must re-roll unmodified Defense 6s, and the attack ignores Regeneration.");

    // "in melee" mirrors of the when-shooting variants: the SAME base effect at the shared hit/save
    // hooks, gated to melee via Condition.IsMelee() (the flip of Not(IsMelee)). Weapon-scoped like their
    // shooting twins. These are the base rules behind the "... in Melee Aura" grants. Rending has no
    // when-shooting twin, so it mirrors the base Rending (AP-on-6 + Regen-ignore) with an added melee gate.

    /// <summary> Unstoppable in melee only: ignores Regeneration when in melee. </summary>
    public static SpecialRuleDefinition UnstoppableInMelee { get; } =
        new SpecialRuleDefinition("Unstoppable in melee",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.IsMelee(),
                    new Effect.IgnoreRule("Regeneration"),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "In melee, this unit's attacks ignore the target's Regeneration.");

    /// <summary> Shred in melee only: +1 wound per unmodified save of 1 when in melee. </summary>
    public static SpecialRuleDefinition ShredInMelee { get; } =
        new SpecialRuleDefinition("Shred in melee",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.IsMelee(),
                    new Effect.AddExtraWound(OnRollValue: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "In melee, each of the defender's unmodified Defense rolls of 1 deals an extra wound.");

    /// <summary> Bane in melee only: defender re-rolls unmodified Defense 6s + ignore Regeneration, in melee. </summary>
    public static SpecialRuleDefinition BaneInMelee { get; } =
        new SpecialRuleDefinition("Bane in melee",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.IsMelee(),
                    new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue()),
                    ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.IsMelee(),
                    new Effect.IgnoreRule("Regeneration"),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "In melee, the defender must re-roll unmodified Defense 6s, and the attack ignores Regeneration.");

    /// <summary> Rending in melee only: per-hit AP(4) on an unmodified 6 + ignore Regeneration, when in melee. </summary>
    public static SpecialRuleDefinition RendingInMelee { get; } =
        new SpecialRuleDefinition("Rending in melee",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.And(new Condition.IsMelee(), new Condition.UnmodifiedRollEquals(6)),
                    new Effect.PerHitSaveModifier(OnRollValue: 6, Delta: -4),
                    ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.IsMelee(),
                    new Effect.IgnoreRule("Regeneration"),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "In melee, an unmodified 6 to hit gives the attack AP(+4), and the attack ignores Regeneration.");

    // Defensive wound-ignore + AP corpus rules (reuse IgnoreWoundOnRoll / save-modifier primitives) ------

    /// <summary>
    /// Resistance: when this unit takes wounds, each is ignored on a die roll of 6+ — or on a 2+ if the
    /// wounds came from a spell (corpus: "If the wounds were from a spell, then they are ignored on a 2+
    /// instead"). Two <see cref="Effect.IgnoreWoundOnRoll"/> entries on the defender (Subject) at
    /// <see cref="EHookID.Shooting_OnSaveRollComplete"/>: an unconditional 6+ and a
    /// <see cref="Condition.IsSpell"/>-gated 2+; on spell damage both fire and the
    /// <see cref="WoundIgnoreSink"/> keeps the best (lowest) threshold, yielding exactly the
    /// "2+ instead" reading.
    /// </summary>
    public static SpecialRuleDefinition Resistance { get; } = new SpecialRuleDefinition("Resistance",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.AllModelsHaveThisRule(),
                new Effect.IgnoreWoundOnRoll(MinRoll: 6),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.And(new Condition.IsSpell(), new Condition.AllModelsHaveThisRule()),
                new Effect.IgnoreWoundOnRoll(MinRoll: 2),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Ignores each wound on a roll of 6+ (2+ if the wounds came from a spell).");

    /// <summary> Protected: mechanically identical to <see cref="Resistance"/> — each wound ignored on a 6+. </summary>
    public static SpecialRuleDefinition Protected { get; } = new SpecialRuleDefinition("Protected",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.AllModelsHaveThisRule(),
                new Effect.IgnoreWoundOnRoll(MinRoll: 6),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Ignores each wound on a roll of 6+.");

    /// <summary>
    /// Piercing Assault: this model gets AP(+1) when charging — the AP facet of <see cref="Thrust"/> alone
    /// (no +1 to hit). A save-roll modifier on the attacker (Actor) at
    /// <see cref="EHookID.Shooting_OnHitRollComplete"/>, gated to a charging melee swing.
    /// </summary>
    public static SpecialRuleDefinition PiercingAssault { get; } = new SpecialRuleDefinition("Piercing Assault",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.IsMelee(), new Condition.IsCharging()),
                new Effect.RollModifier(ERollKind.Save, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When charging, this model's melee attacks gain AP(+1).");

    /// <summary>
    /// Piercing Hunter: this model's weapons get AP(+1) when shooting at enemies over 9" away. The same
    /// save-roll-modifier AP shape as <see cref="PiercingAssault"/>, gated by distance instead of charge
    /// (the <c>DistanceGreaterThan(9)</c> condition naturally excludes melee, like Artillery).
    /// </summary>
    public static SpecialRuleDefinition PiercingHunter { get; } = new SpecialRuleDefinition("Piercing Hunter",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.DistanceGreaterThan(9f),
                new Effect.RollModifier(ERollKind.Save, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When shooting at enemies over 9\" away, this model's weapons gain AP(+1).");

    /// <summary>
    /// Shielded: +1 to this unit's defense rolls — a defensive save bonus. A Subject-seat
    /// <see cref="Effect.RollModifier"/>(Save, +1) at <see cref="EHookID.Shooting_OnHitRollComplete"/>,
    /// the seam where <c>RollToHitStage</c> now evaluates the defender; its modifier folds into the save
    /// threshold alongside the attacker's AP (a +1 defense and an attacker's -N net). Applies to both
    /// shooting and melee (the hit stages are shared). The corpus's "against hits that are NOT from spells"
    /// facet is enforced by <see cref="Condition.IsNotSpell"/>: the spell-damage pipeline evaluates the
    /// defender at this hook too (so Fortified works vs spells), and this condition is what keeps
    /// Shielded's bonus out of it.
    /// </summary>
    public static SpecialRuleDefinition Shielded { get; } = new SpecialRuleDefinition("Shielded",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.IsNotSpell(), new Condition.AllModelsHaveThisRule()),
                new Effect.RollModifier(ERollKind.Save, Delta: +1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "+1 to this unit's Defense rolls.");

    /// <summary>
    /// Fortified: incoming hits count as having AP reduced by 1, to a minimum of AP(0). Unlike
    /// <see cref="Shielded"/>'s flat +1 defense, it only cancels existing armor penetration — it does
    /// nothing against an AP(0) hit. A defender (Subject) <see cref="Effect.ReduceArmorPenetration"/>(1)
    /// at <see cref="EHookID.Shooting_OnHitRollComplete"/>; <c>DetermineSaveRollsNeededStage</c> clamps the
    /// weapon's AP by it (floored at 0). Rule-granted AP (Rending/Thrust) is folded into the save modifier
    /// and not reduced here — a noted approximation of "the hit's AP".
    /// </summary>
    public static SpecialRuleDefinition Fortified { get; } = new SpecialRuleDefinition("Fortified",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.AllModelsHaveThisRule(),
                new Effect.ReduceArmorPenetration(1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Incoming hits have their AP reduced by 1, to a minimum of 0.");

    // Wound-injection sink (AssignWoundsStage) -----------------------------------

    /// <summary>
    /// Shred (weapon rule): for each of the defender's unmodified save rolls of 1 ("1 to block"), the
    /// attack deals 1 extra wound. Reads the save-roll histogram at
    /// <see cref="EHookID.Shooting_OnSaveRollComplete"/> via <see cref="Effect.AddExtraWound"/>, which
    /// queues <see cref="RuleOperation.InsertExtraWounds"/>; AssignWoundsStage folds it through the
    /// <see cref="WoundInjectionSink"/> and adds the wounds (after Deadly's clump confinement, before
    /// Regeneration so the defender may still ignore them). The wound-side mirror of Surge.
    /// </summary>
    public static SpecialRuleDefinition Shred { get; } = new SpecialRuleDefinition("Shred",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.AddExtraWound(OnRollValue: 1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "Each of the defender's unmodified Defense rolls of 1 deals an extra wound.");

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
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Each model in the unit has multiple wounds instead of one.");

    /// <summary>
    /// Hero (#006): a marker rule. The hero's structural facets (joining a host unit at army setup, and
    /// the stat-divergence seams — wounds-last, morale at the hero's Quality, last-model Defense, firing
    /// at the hero's Quality) are not expressible as a single-unit hook Effect: the join needs cross-unit
    /// information (the host) that <see cref="EHookID.Lifecycle_OnUnitCreated"/> can't see, so it runs as
    /// explicit setup code in <see cref="HeroJoinResolver"/>. This definition exists so the army-list rule
    /// name "Hero" resolves (rather than skip+log) and so the host/hero eligibility checks have a stable
    /// <see cref="SpecialRuleDefinition"/> identity to test against.
    /// </summary>
    public static SpecialRuleDefinition Hero { get; } = new SpecialRuleDefinition("Hero",
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "A leader that joins a friendly unit; the unit takes morale at the hero's Quality, and the hero is wounded last.");

    // Spell-token economy (round-start grant -> StartOfRoundExtraActionStage) ------

    /// <summary>
    /// Caster(X): at the start of every round the unit gains X spell tokens, used to cast spells from
    /// its army's spell list (the "Cast" action, #033). A passive rule at
    /// <see cref="EHookID.Round_OnRoundStart"/> that grants <c>Arg(0)</c> <see cref="TokenType.SpellTokens"/>;
    /// the grant is fired and applied per round by <see cref="Stages.StartOfRoundExtraActionStage"/>.
    /// Tokens carry over between rounds, so the clear trigger is <see cref="TokenClearTrigger.ManualOnly"/>
    /// (not RoundEnd) and the 6-token cap (<see cref="GameWideConstants.MAX_SPELL_TOKENS"/>) is clamped at
    /// grant time, not by clearing. Casting itself (spell selection, target, the 4+ roll, the ±1 friendly
    /// assist) is the Cast stage, not this definition — this just seeds the per-round token pool.
    /// </summary>
    public static SpecialRuleDefinition Caster { get; } = new SpecialRuleDefinition("Caster",
        new[]
        {
            new HookEntry(EHookID.Round_OnRoundStart,
                new Condition.Always(),
                new Effect.GrantToken(TokenType.SpellTokens, new ValueSource.Arg(0),
                    new TokenClearTrigger.ManualOnly()),
                ELifetime.UntilEndOfGame),
            // The capability half: funding a pool and being ALLOWED to spend it are separate questions,
            // and the stages ask this one (CapabilityRuleQueries.CanCast) rather than testing for this rule
            // by name — so a second caster-conferring rule needs no engine change. See Effect.EnableCasting.
            new HookEntry(EHookID.Lifecycle_OnCapabilityQuery,
                new Condition.Always(),
                new Effect.EnableCasting(),
                ELifetime.UntilEndOfGame),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "At the start of each round, gains spell tokens to cast spells from its army's spell list.");

    /// <summary>
    /// Transport(X) (#035): a marker rule, like <see cref="Hero"/>. A transport carries friendly units
    /// inside it; the capacity X (in "spaces") is the rule's <c>Arg(0)</c>, read off the attachment by
    /// <see cref="TransportUtilities.GetCapacity"/>. The behavior — occupancy (a cross-unit
    /// <see cref="Foundation.TokenType.EmbarkedIn"/> token), deploy-time loading, embark/disembark move
    /// actions, the inside↔outside targeting scope (free, via off-battlefield occupants), and mid-combat
    /// destruction spillout — is a first-class engine subsystem, not a composed hook Effect: it needs
    /// cross-unit relationships and new action/combat-flow verbs that no single-unit hook can express
    /// (the same reason <see cref="Hero"/>'s join is engine code). This definition exists so the rule has
    /// a stable identity to attach and test against.
    ///
    /// In <see cref="All"/> so the army-builder picker offers it and army-load resolves "Transport" by name.
    /// It is a <em>marker-with-arg</em>: the capacity is its <c>Arg(0)</c>, but no hook Effect references it
    /// (engine code reads it), so it declares <c>EngineArgumentCount: 1</c> — without that
    /// <see cref="RuleArgumentArity"/> would infer 0 args and the picker would treat it as non-numeric.
    /// Unit-scoped (the default).
    /// </summary>
    public static SpecialRuleDefinition Transport { get; } = new SpecialRuleDefinition("Transport",
        new[]
        {
            // Being a transport is a CAPABILITY the stages ask for, not this rule's identity - so a second
            // rule conferring a hold needs no engine change. The capacity rides the answer because "is it a
            // transport" and "how big is the hold" are one question. See Effect.EnableTransport.
            new HookEntry(EHookID.Lifecycle_OnCapabilityQuery,
                new Condition.Always(),
                new Effect.EnableTransport(new ValueSource.Arg(0)),
                ELifetime.UntilEndOfGame),
        },
        Array.Empty<ActivatedAbility>(),
        EngineArgumentCount: 1,
        Valence: EValence.Positive,
        Description: "Carries friendly units inside it (capacity in spaces); they embark and disembark via move actions.");

    /// <summary> Canonical name of the engine-internal Disembark ability (#035). </summary>
    public const string DisembarkRuleName = "Disembark";

    /// <summary>
    /// Disembark (#035): an engine-internal activated ability attached to every unit at army-load (in
    /// <c>FDGServer</c>), gated to surface only while the unit is embarked (an
    /// <see cref="Foundation.TokenType.EmbarkedIn"/> token present). Offered at
    /// <see cref="EHookID.Activation_OnActionChoice"/> so it appears as a "Disembark" action; because its
    /// effect is movement (place within 6" of the transport + un-embark), <c>ChooseActionStage</c> routes
    /// it by name to <c>DisembarkStage</c> rather than the generic token-op resolver. Not in
    /// <see cref="All"/> — it isn't an army-list rule players pick; the engine attaches it universally.
    /// Being a durable rule gated by the *serialized* token (not a transient attach), it is restored by the
    /// #094 resume rule-rehydration fix.
    /// </summary>
    public static SpecialRuleDefinition Disembark { get; } = new SpecialRuleDefinition(DisembarkRuleName,
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.Disembark(),
                new Condition.TokenPresent(TokenType.EmbarkedIn)),
        },
        Valence: EValence.Neutral,
        Description: "Leave the transport this unit is embarked in, placing within 6\" of it.");

    /// <summary> Canonical name of the engine-internal Embark ability (#035 slice D). </summary>
    public const string EmbarkRuleName = "Embark";

    /// <summary>
    /// Embark (#035 slice D): the mid-game counterpart of <see cref="Disembark"/> — an engine-internal
    /// activated ability attached to every unit at army-load, surfacing "Embark into &lt;transport&gt;" so a
    /// unit can board a friendly transport on its activation. Unlike Disembark's clean token gate, embark's
    /// availability is *spatial* ("a friendly transport with room within move-range"), which the
    /// architecture has no data condition for — so <c>AvailableWhen</c> is <c>Always</c> and
    /// <c>ChooseActionStage</c> applies the spatial gate in engine code (the same way Charge is gated by
    /// enemy-in-range) before surfacing it, then routes it by name to <c>EmbarkStage</c>. Not in
    /// <see cref="All"/> — not a player-picked rule. Effect is the no-op <see cref="Effect.Embark"/> marker.
    /// </summary>
    public static SpecialRuleDefinition Embark { get; } = new SpecialRuleDefinition(EmbarkRuleName,
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.Embark(),
                new Condition.Always()),
        },
        Valence: EValence.Neutral,
        Description: "Board a friendly transport that has room, within move range.");

    /// <summary> Canonical name of the Delayed Action rule (#197). </summary>
    public const string DelayedActionRuleName = "Delayed Action";

    /// <summary>
    /// Delayed Action (#197): "Once per round, if your opponent has more units left to activate than you,
    /// this model's unit may pass its turn instead of activating (may still be activated later)." An engine
    /// marker (no dispatch hooks or abilities) - like <see cref="Hero"/>/<see cref="Transport"/>, its effect
    /// is enacted stage-side, not through the rule pipeline. <c>ChooseUnitToActivateStage</c> detects it by
    /// name on the chosen unit and, when the opponent has more units left to activate and the player hasn't
    /// already delayed this round (a per-player <see cref="Foundation.TokenType.DelayedActionUsed"/> scan),
    /// offers to hold the unit back: the turn passes to the opponent with the unit still in the pool.
    /// Allowlisted in the catalog fire-lint (a marker with no operations).
    /// </summary>
    public static SpecialRuleDefinition DelayedAction { get; } = new SpecialRuleDefinition(DelayedActionRuleName,
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Once per round, if your opponent has more units left to activate than you, this unit " +
                     "may hold back (pass the turn) and activate later instead.");

    /// <summary> Canonical name of the Re-Deployment rule (#197 P21). </summary>
    public const string ReDeploymentRuleName = "Re-Deployment";

    /// <summary>
    /// Re-Deployment (#197 P21): "After all other units are deployed (excluding units that were set aside),
    /// you may remove up to two friendly units from the table and deploy them again. Players alternate in
    /// placing Re-Deployment units, starting with the player that activates next." An engine marker (no
    /// dispatch hooks or abilities) - like <see cref="DelayedAction"/>, its effect is a deployment-phase
    /// sub-stage, not a rule-pipeline op. <c>ReDeploymentStage</c> (after normal deployment, before the
    /// set-aside Scout placement) detects it by name to compute each player's budget - owner ruling: TWO
    /// redeploys per Re-Deployment unit owned, stacking - then alternates the players (starting with whoever
    /// activates next = the head of the deployment roll order) offering each a friendly on-table unit to
    /// pick up and re-place in its deployment zone. Allowlisted in the catalog fire-lint (a marker with no
    /// operations).
    /// </summary>
    public static SpecialRuleDefinition ReDeployment { get; } = new SpecialRuleDefinition(ReDeploymentRuleName,
        new[]
        {
            // A capability the re-deployment pass asks for rather than testing for this rule. See
            // Effect.EnableReDeployment.
            new HookEntry(EHookID.Lifecycle_OnCapabilityQuery,
                new Condition.Always(),
                new Effect.EnableReDeployment(),
                ELifetime.UntilEndOfGame),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "After deployment, you may pick up and re-place up to two friendly units per unit with " +
                     "this rule. Players alternate, starting with whoever activates next.");

    /// <summary> Canonical name of the Retaliate rule (#197 P11). </summary>
    public const string RetaliateRuleName = "Retaliate";

    /// <summary>
    /// Retaliate(X) (#197 P11): "when this model takes a wound in melee, the attacker takes X hits per wound
    /// taken." An engine marker (no dispatch hooks or abilities) - its effect is a post-melee reflect,
    /// resolved stage-side, not through the rule pipeline. <c>ResolveMeleeReflectStage</c> (after the melee
    /// resolves) counts, per MODEL carrying the rule, the wounds it took this melee - the per-model
    /// attribution the owner ruled for - and deals X hits per wound back at the attacking unit through the
    /// real save/wound pipeline. Arg(0) is X. Allowlisted in the catalog fire-lint (a marker with no
    /// operations, like Transport's capacity marker).
    /// </summary>
    public static SpecialRuleDefinition Retaliate { get; } = new SpecialRuleDefinition(RetaliateRuleName,
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When a model with this rule takes a wound in melee, the attacker takes X hits per wound.");

    /// <summary> Canonical name of the Deathstrike rule (#197 P11). </summary>
    public const string DeathstrikeRuleName = "Deathstrike";

    /// <summary>
    /// Deathstrike(X) (#197 P11): "if this model is killed in melee, the attacking unit takes X hits." The
    /// death-triggered sibling of <see cref="Retaliate"/> - the same post-melee reflect, but keyed on the
    /// rule-bearing MODEL being killed this melee (X hits per killed model) rather than on wounds taken.
    /// Marker rule; resolved by <c>ResolveMeleeReflectStage</c>. Arg(0) is X. Allowlisted in the fire-lint.
    /// </summary>
    public static SpecialRuleDefinition Deathstrike { get; } = new SpecialRuleDefinition(DeathstrikeRuleName,
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "If a model with this rule is killed in melee, the attacking unit takes X hits.");

    /// <summary> Canonical name of the Self-Destruct rule (#197 P11). </summary>
    public const string SelfDestructRuleName = "Self-Destruct";

    /// <summary>
    /// Self-Destruct(X) (#197 P11): "if this model is killed in melee, the attacking unit takes X hits. If
    /// this model survives melee, after both sides have finished attacking, it is immediately killed, and the
    /// enemy unit takes X hits." So every rule-bearing model that entered the melee deals X hits at the enemy
    /// - whether it died fighting or is self-destructed at the end - and any survivor is killed. The
    /// death-or-self-kill twin of <see cref="Deathstrike"/>; resolved by <c>ResolveMeleeReflectStage</c>,
    /// which also enacts the self-kill. Marker rule; Arg(0) is X. Allowlisted in the fire-lint.
    /// </summary>
    public static SpecialRuleDefinition SelfDestruct { get; } = new SpecialRuleDefinition(SelfDestructRuleName,
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "A model with this rule that fights in melee deals X hits to the enemy; if it survives, " +
                     "it is then killed.");

    /// <summary> Canonical name of the Teleport ability (#197). </summary>
    public const string TeleportRuleName = "Teleport";

    /// <summary>
    /// Teleport (#197): "once per activation, before attacking, you may place this model anywhere fully
    /// within 6&quot; of its position." Unlike Disembark/Embark this IS a book-listed faction rule (Knight
    /// Brothers, Eternal Dynasty, ...), so it lives in <see cref="All"/> and resolves at army-load wherever a
    /// book references "Teleport" - but its effect is a placement, not a token op, so it follows the same
    /// engine-stage pattern: offered at <see cref="EHookID.Activation_OnActionChoice"/> as a menu action, then
    /// routed by name to <c>TeleportStage</c> (the generic custom-action resolver is token-ops only).
    /// <see cref="Cost.OncePerActivation"/> makes it a single reposition per activation; the "before
    /// attacking" gate (not-yet-attacked) is applied in engine code by <c>ChooseActionStage</c>, like Embark's
    /// spatial gate. Fully layered - it sets neither HasMoved nor HasAttacked - so a unit may move, teleport,
    /// then still shoot; the teleport is bonus repositioning that does not count toward the move-shoot cap.
    /// </summary>
    public static SpecialRuleDefinition Teleport { get; } = new SpecialRuleDefinition(TeleportRuleName,
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.Teleport(),
                // #267: the whole unit teleports, so the whole unit must have the rule. Without this a hero
                // carrying Teleport who joins a squad that lacks it teleports the entire squad.
                new Condition.AllModelsHaveThisRule()),
        },
        Valence: EValence.Neutral,
        Description: "Once per activation, before attacking, place each model fully within 6\" of its position.");

    // Triggered-move primitive (DeployUnitStage offer -> MovementExecutor) --------

    /// <summary>
    /// Vanguard: once per game, after this unit deploys it may immediately move up to 9".
    /// An activated ability offered at <see cref="EHookID.Deployment_OnUnitDeployed"/>; accepting
    /// it queues an <see cref="RuleOperation.InvokeTriggeredMove"/> the engine enacts via the
    /// movement subsystem.
    /// </summary>
    public static SpecialRuleDefinition Vanguard { get; } = new SpecialRuleDefinition("Vanguard",
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Deployment_OnUnitDeployed, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.TriggeredMove(MaxInches: 9f, IsOptional: true),
                // #267: the post-deploy move carries every model, so every model must have Vanguard.
                new Condition.AllModelsHaveThisRule()),
        },
        Valence: EValence.Positive,
        Description: "Once per game, immediately after deploying, this unit may move up to 9\".");

    /// <summary>
    /// Fanatic (#197 P21): after this unit deploys, it may be PLACED anywhere fully within 9" of its
    /// position. Vanguard's deploy-hook shape, but a placement rather than a move (the owner's
    /// reposition-is-a-placement ruling, and the corpus word "placed"): the effect emits a
    /// <see cref="RuleOperation.RepositionModels"/> that <c>DeployUnitStage</c> folds into a
    /// per-model-radius <see cref="StageResolution.Requests.PlaceObjectsRequest{T}"/>. Gated
    /// <see cref="Cost.OncePerGame"/> like Vanguard - deployment happens once, so the gate is naturally
    /// spent, and it keeps the offer from re-firing on a Scout/Ambush re-placement.
    /// </summary>
    public static SpecialRuleDefinition Fanatic { get; } = new SpecialRuleDefinition("Fanatic",
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Deployment_OnUnitDeployed, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.RepositionOnDeploy(MaxInches: 9f),
                // #267: the whole unit is re-placed, so the whole unit must have Fanatic.
                new Condition.AllModelsHaveThisRule()),
        },
        Valence: EValence.Positive,
        Description: "After deploying, this unit may be placed anywhere fully within 9\" of its position.");

    /// <summary>
    /// Harassing: after this unit shoots — or after it is attacked in melee — it may immediately move up
    /// to 3" (optional). Two passive <see cref="HookEntry"/>s: one at
    /// <see cref="EHookID.Shooting_OnPostShoot"/> (fired once per shoot action by <c>PostShootStage</c>)
    /// and one at <see cref="EHookID.Melee_OnPostMelee"/> (fired for the charged unit by
    /// <c>PostMeleeStage</c> once the melee fully resolves). Each queues an optional
    /// <see cref="Effect.TriggeredMove"/> the engine enacts through the movement subsystem; the bearer is
    /// the moving unit, so the move is a self-move directed by its own owner (a reactive disengage in the
    /// melee case).
    /// </summary>
    public static SpecialRuleDefinition Harassing { get; } = new SpecialRuleDefinition("Harassing",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnPostShoot,
                new Condition.Always(),
                new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Melee_OnPostMelee,
                new Condition.Always(),
                new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "After shooting or being in melee, this unit may immediately move up to 3\".");

    // Post-combat-move family (same TriggeredMove seam as Harassing) --------------
    // The corpus splits the "may move 3\" after combat" rule into shooting-only, melee-only, and both
    // variants under several faction names. All share Harassing's shape; they differ only in which of the
    // two post-combat hooks they carry. NB the corpus wording is "ONCE PER ROUND" — these (like Harassing)
    // currently fire once per shoot ACTION and once per resolved melee, with no per-round gate; see the
    // #100 ledger's deferred-facet note (a unit charged multiple times can move after each melee).

    /// <summary>
    /// Hit &amp; Run Shooter: after this unit shoots, it may move up to 3" (optional). The shooting-only
    /// member of the post-combat-move family — identical to <see cref="Harassing"/>'s shooting half.
    /// </summary>
    public static SpecialRuleDefinition HitAndRunShooter { get; } = new SpecialRuleDefinition("Hit & Run Shooter",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnPostShoot,
                new Condition.Always(),
                new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "After shooting, this unit may immediately move up to 3\".");

    /// <summary>
    /// Hit &amp; Run Fighter: after this unit is in melee, the charged unit may move up to 3" (optional).
    /// The melee-only member of the family — identical to <see cref="Harassing"/>'s melee half.
    /// </summary>
    public static SpecialRuleDefinition HitAndRunFighter { get; } = new SpecialRuleDefinition("Hit & Run Fighter",
        new[]
        {
            new HookEntry(EHookID.Melee_OnPostMelee,
                new Condition.Always(),
                new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "After a melee, this unit may immediately move up to 3\".");

    /// <summary>
    /// Hit &amp; Run: after this unit shoots OR is in melee, it may move up to 3" (optional). Both
    /// post-combat hooks — mechanically identical to <see cref="Harassing"/> and <see cref="Guerrilla"/>
    /// (the corpus uses different names per faction).
    /// </summary>
    public static SpecialRuleDefinition HitAndRun { get; } = new SpecialRuleDefinition("Hit & Run",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnPostShoot,
                new Condition.Always(),
                new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Melee_OnPostMelee,
                new Condition.Always(),
                new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "After shooting or a melee, this unit may immediately move up to 3\".");

    /// <summary>
    /// Guerrilla: the Rebel Guerrillas' name for <see cref="HitAndRun"/> — move up to 3" after shooting
    /// or melee (optional, both hooks). Same mechanics, faction-specific name.
    /// </summary>
    public static SpecialRuleDefinition Guerrilla { get; } = new SpecialRuleDefinition("Guerrilla",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnPostShoot,
                new Condition.Always(),
                new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Melee_OnPostMelee,
                new Condition.Always(),
                new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "After shooting or a melee, this unit may immediately move up to 3\".");

    // Boost variants: "if the unit has the base rule, move 6\" instead of 3\"". Authored as a separate
    // post-combat-move rule emitting a 6" move gated on UnitHasRule(base); PostCombatMoveGate coalesces it
    // with the base rule's own 3" move into a SINGLE 6" move (the max budget), so "6 instead of 3" falls
    // out of the gate's max(). NB UnitHasRule is the unit-level gate the architecture provides — the
    // corpus wording "if most MODELS have X" is a per-model majority (#093) not distinguished here, in
    // keeping with the catalog treating "units where all models have this rule" rules as unit-level.

    /// <summary>
    /// Harassing Boost: if the unit has <see cref="Harassing"/>, its post-combat move is 6" instead of 3".
    /// </summary>
    public static SpecialRuleDefinition HarassingBoost { get; } = new SpecialRuleDefinition("Harassing Boost",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnPostShoot,
                new Condition.UnitHasRule("Harassing"),
                new Effect.TriggeredMove(MaxInches: 6f, IsOptional: true),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Melee_OnPostMelee,
                new Condition.UnitHasRule("Harassing"),
                new Effect.TriggeredMove(MaxInches: 6f, IsOptional: true),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "If the unit has Harassing, its post-combat move is 6\" instead of 3\".");

    /// <summary>
    /// Guerrilla Boost: if the unit has <see cref="Guerrilla"/>, its post-combat move is 6" instead of 3".
    /// </summary>
    public static SpecialRuleDefinition GuerrillaBoost { get; } = new SpecialRuleDefinition("Guerrilla Boost",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnPostShoot,
                new Condition.UnitHasRule("Guerrilla"),
                new Effect.TriggeredMove(MaxInches: 6f, IsOptional: true),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Melee_OnPostMelee,
                new Condition.UnitHasRule("Guerrilla"),
                new Effect.TriggeredMove(MaxInches: 6f, IsOptional: true),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "If the unit has Guerrilla, its post-combat move is 6\" instead of 3\".");

    // Aura variants: "this model and its unit get X" — Effect.Aura(X) at unit creation via the UnitAura
    // factory; the grant projects unit-wide via the read-back (RuleEvaluator.CollectGrantedRules), so the
    // granted rule fires for the whole unit at whatever hook it carries. The post-combat-move auras:
    /// <summary> Hit &amp; Run Shooter Aura: this model and its unit gain <see cref="HitAndRunShooter"/>. </summary>
    public static SpecialRuleDefinition HitAndRunShooterAura { get; } = UnitAura("Hit & Run Shooter Aura", "Hit & Run Shooter");
    /// <summary> Hit &amp; Run Fighter Aura: this model and its unit gain <see cref="HitAndRunFighter"/>. </summary>
    public static SpecialRuleDefinition HitAndRunFighterAura { get; } = UnitAura("Hit & Run Fighter Aura", "Hit & Run Fighter");
    /// <summary> Harassing Boost Aura: this model and its unit gain <see cref="HarassingBoost"/>. </summary>
    public static SpecialRuleDefinition HarassingBoostAura { get; } = UnitAura("Harassing Boost Aura", "Harassing Boost");
    /// <summary> Guerrilla Boost Aura: this model and its unit gain <see cref="GuerrillaBoost"/>. </summary>
    public static SpecialRuleDefinition GuerrillaBoostAura { get; } = UnitAura("Guerrilla Boost Aura", "Guerrilla Boost");

    // General aura cluster: "this model and its unit get <rule>" for rules already in the catalog. Pure
    // data — each grants an existing base by its EXACT registered name (the resolver is case-sensitive,
    // hence the lowercase "...when shooting" grants under capitalized aura names). Auras whose base is NOT
    // yet cataloged (Courage, Melee Shrouding, Resistance, the "in melee" combat-kind mirrors, …) wait on
    // their base rule and are deliberately omitted here.
    /// <summary> Regeneration Aura: grants <see cref="Regeneration"/> unit-wide. </summary>
    public static SpecialRuleDefinition RegenerationAura { get; } = UnitAura("Regeneration Aura", "Regeneration");
    /// <summary> #197 Teleport Aura: "this model and its unit get Teleport" - grants <see cref="Teleport"/>
    /// unit-wide (the granted activated ability surfaces in Choose Action for the whole unit). </summary>
    public static SpecialRuleDefinition TeleportAura { get; } = UnitAura("Teleport Aura", "Teleport");
    /// <summary> Furious Aura: grants <see cref="Furious"/> unit-wide. </summary>
    public static SpecialRuleDefinition FuriousAura { get; } = UnitAura("Furious Aura", "Furious");
    /// <summary> Stealth Aura: grants <see cref="Stealth"/> unit-wide. </summary>
    public static SpecialRuleDefinition StealthAura { get; } = UnitAura("Stealth Aura", "Stealth");
    /// <summary> Scout Aura: grants <see cref="Scout"/> unit-wide. </summary>
    public static SpecialRuleDefinition ScoutAura { get; } = UnitAura("Scout Aura", "Scout");
    /// <summary> Relentless Aura: grants <see cref="Relentless"/> unit-wide. </summary>
    public static SpecialRuleDefinition RelentlessAura { get; } = UnitAura("Relentless Aura", "Relentless");
    /// <summary> Ambush Aura: grants <see cref="Ambush"/> unit-wide. </summary>
    public static SpecialRuleDefinition AmbushAura { get; } = UnitAura("Ambush Aura", "Ambush");
    /// <summary> Counter-Attack Aura: grants <see cref="CounterAttack"/> unit-wide. </summary>
    public static SpecialRuleDefinition CounterAttackAura { get; } = UnitAura("Counter-Attack Aura", "Counter-Attack");
    /// <summary> Evasive Aura: grants <see cref="Evasive"/> unit-wide. </summary>
    public static SpecialRuleDefinition EvasiveAura { get; } = UnitAura("Evasive Aura", "Evasive");
    /// <summary> Fast Aura: grants <see cref="Fast"/> unit-wide. </summary>
    public static SpecialRuleDefinition FastAura { get; } = UnitAura("Fast Aura", "Fast");
    /// <summary> Fearless Aura: grants <see cref="Fearless"/> unit-wide. </summary>
    public static SpecialRuleDefinition FearlessAura { get; } = UnitAura("Fearless Aura", "Fearless");
    /// <summary> Melee Evasion Aura: grants <see cref="MeleeEvasion"/> unit-wide. </summary>
    public static SpecialRuleDefinition MeleeEvasionAura { get; } = UnitAura("Melee Evasion Aura", "Melee Evasion");
    /// <summary> Rapid Advance Aura: grants <see cref="RapidAdvance"/> unit-wide. </summary>
    public static SpecialRuleDefinition RapidAdvanceAura { get; } = UnitAura("Rapid Advance Aura", "Rapid Advance");
    /// <summary> Rapid Charge Aura: grants <see cref="RapidCharge"/> unit-wide. </summary>
    public static SpecialRuleDefinition RapidChargeAura { get; } = UnitAura("Rapid Charge Aura", "Rapid Charge");
    /// <summary> Rapid Rush Aura: grants <see cref="RapidRush"/> unit-wide. </summary>
    public static SpecialRuleDefinition RapidRushAura { get; } = UnitAura("Rapid Rush Aura", "Rapid Rush");
    /// <summary> Bane when Shooting Aura: grants <see cref="BaneWhenShooting"/> ("Bane when shooting") unit-wide. </summary>
    public static SpecialRuleDefinition BaneWhenShootingAura { get; } = UnitAura("Bane when Shooting Aura", "Bane when shooting");
    /// <summary> Shred when Shooting Aura: grants <see cref="ShredWhenShooting"/> ("Shred when shooting") unit-wide. </summary>
    public static SpecialRuleDefinition ShredWhenShootingAura { get; } = UnitAura("Shred when Shooting Aura", "Shred when shooting");
    /// <summary> Unstoppable when Shooting Aura: grants <see cref="UnstoppableWhenShooting"/> ("Unstoppable when shooting") unit-wide. </summary>
    public static SpecialRuleDefinition UnstoppableWhenShootingAura { get; } = UnitAura("Unstoppable when Shooting Aura", "Unstoppable when shooting");
    /// <summary> Courage Aura: grants <see cref="Courage"/> (+1 to morale tests) unit-wide. </summary>
    public static SpecialRuleDefinition CourageAura { get; } = UnitAura("Courage Aura", "Courage");
    /// <summary> Bane in Melee Aura: grants <see cref="BaneInMelee"/> unit-wide. </summary>
    public static SpecialRuleDefinition BaneInMeleeAura { get; } = UnitAura("Bane in Melee Aura", "Bane in melee");
    /// <summary> Rending in Melee Aura: grants <see cref="RendingInMelee"/> unit-wide. </summary>
    public static SpecialRuleDefinition RendingInMeleeAura { get; } = UnitAura("Rending in Melee Aura", "Rending in melee");
    /// <summary> Shred in Melee Aura: grants <see cref="ShredInMelee"/> unit-wide. </summary>
    public static SpecialRuleDefinition ShredInMeleeAura { get; } = UnitAura("Shred in Melee Aura", "Shred in melee");
    /// <summary> Unstoppable in Melee Aura: grants <see cref="UnstoppableInMelee"/> unit-wide. </summary>
    public static SpecialRuleDefinition UnstoppableInMeleeAura { get; } = UnitAura("Unstoppable in Melee Aura", "Unstoppable in melee");
    /// <summary> Resistance Aura: grants <see cref="Resistance"/> unit-wide. </summary>
    public static SpecialRuleDefinition ResistanceAura { get; } = UnitAura("Resistance Aura", "Resistance");
    /// <summary> Protected Aura: grants <see cref="Protected"/> unit-wide. </summary>
    public static SpecialRuleDefinition ProtectedAura { get; } = UnitAura("Protected Aura", "Protected");
    /// <summary> Piercing Assault Aura: grants <see cref="PiercingAssault"/> unit-wide. </summary>
    public static SpecialRuleDefinition PiercingAssaultAura { get; } = UnitAura("Piercing Assault Aura", "Piercing Assault");
    /// <summary> Piercing Hunter Aura: grants <see cref="PiercingHunter"/> unit-wide. </summary>
    public static SpecialRuleDefinition PiercingHunterAura { get; } = UnitAura("Piercing Hunter Aura", "Piercing Hunter");
    /// <summary> Shielded Aura: grants <see cref="Shielded"/> unit-wide. </summary>
    public static SpecialRuleDefinition ShieldedAura { get; } = UnitAura("Shielded Aura", "Shielded");
    /// <summary> Fortified Aura: grants <see cref="Fortified"/> unit-wide. </summary>
    public static SpecialRuleDefinition FortifiedAura { get; } = UnitAura("Fortified Aura", "Fortified");
    /// <summary> Increased Shooting Range Aura (#102): grants <see cref="IncreasedShootingRange"/> unit-wide. </summary>
    public static SpecialRuleDefinition IncreasedShootingRangeAura { get; } = UnitAura("Increased Shooting Range Aura", "Increased Shooting Range");
    /// <summary> Ranged Shrouding Aura (#102): grants <see cref="RangedShrouding"/> unit-wide. </summary>
    public static SpecialRuleDefinition RangedShroudingAura { get; } = UnitAura("Ranged Shrouding Aura", "Ranged Shrouding");
    /// <summary> Melee Shrouding Aura (#029): grants <see cref="MeleeShrouding"/> unit-wide. </summary>
    public static SpecialRuleDefinition MeleeShroudingAura { get; } = UnitAura("Melee Shrouding Aura", "Melee Shrouding");

    // Deferred-deployment primitive (PlaceDeferredUnitsStage) ---------------------

    /// <summary>
    /// Scout: this unit is set aside during normal deployment and placed after all other units,
    /// within 12" of its deployment zone (forward deploy). A passive rule at
    /// <see cref="EHookID.Deployment_OnPreDeploymentSelect"/> that queues
    /// <see cref="RuleOperation.DeferDeployment"/>; the deployment subsystem reserves the unit and
    /// the Scout pass places it forward.
    /// </summary>
    public static SpecialRuleDefinition Scout { get; } = new SpecialRuleDefinition("Scout",
        new[]
        {
            new HookEntry(EHookID.Deployment_OnPreDeploymentSelect,
                new Condition.Always(),
                new Effect.DeferDeployment(EDeferTiming.AfterNormalDeployment, PlacementRangeInches: 12f),
                ELifetime.UntilEndOfGame),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Deploys after all other units, up to 12\" forward of its deployment zone.");

    /// <summary>
    /// Ambush: this unit is kept in reserve instead of deploying normally, and the owner may bring it
    /// on at the start of any round after the first, placed anywhere over 9" from enemy units. A passive
    /// rule at <see cref="EHookID.Deployment_OnPreDeploymentSelect"/> that queues
    /// <see cref="RuleOperation.DeferDeployment"/> with <see cref="EDeferTiming.LaterRound"/>; the
    /// round-start arrival pass places it.
    /// </summary>
    public static SpecialRuleDefinition Ambush { get; } = new SpecialRuleDefinition("Ambush",
        new[]
        {
            new HookEntry(EHookID.Deployment_OnPreDeploymentSelect,
                new Condition.Always(),
                new Effect.DeferDeployment(EDeferTiming.LaterRound, PlacementRangeInches: 9f),
                ELifetime.UntilEndOfGame),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Kept in reserve; may arrive from round 2 onward, placed over 9\" from enemy units.");

    // Reactivation primitive (DeterminePlayerTurnStage offer -> activation list) ----

    /// <summary>
    /// Martial Prowess: once per game, when the engine asks the owner which unit to activate next,
    /// this already-activated unit may activate a second time. An activated ability offered at
    /// <see cref="EHookID.Activation_OnNextActivatorRequested"/>; accepting it queues an
    /// <see cref="RuleOperation.InvokeReactivate"/> that the activation stage reads to re-add the
    /// unit to the round's unactivated pool.
    ///
    /// Like <see cref="RuleOperation.DeferDeployment"/>, the reactivate operation stays a plain marker
    /// (NOT an <see cref="RuleOperation.InvokeTriggeredMove">ExecutableOperation</see>): it mutates
    /// activation-turn-scoped state (the round's unactivated pool) that the <c>IOperationServices</c>
    /// seam — which only sees <c>IGameContext</c> — can't reach, so the stage applies it directly.
    /// </summary>
    public static SpecialRuleDefinition MartialProwess { get; } = new SpecialRuleDefinition("Martial Prowess",
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Activation_OnNextActivatorRequested, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.Reactivate(),
                // #267: the extra activation belongs to the whole unit, so every model must have the rule.
                new Condition.AllModelsHaveThisRule()),
        },
        Valence: EValence.Positive,
        Description: "Once per game, this unit may activate a second time.");

    // Mid-move attack primitive (StrafingStage offer -> save+wound sub-pipeline) ---

    /// <summary>
    /// Strafing (#197): "Once per activation, when this model moves through enemy units, pick one of them
    /// and attack it with this weapon as if it was shooting. This weapon may only be used in this way."
    /// An activated ability at <see cref="EHookID.Movement_OnMoveThroughEnemy"/>; accepting it queues an
    /// <see cref="RuleOperation.InvokeWeaponAttack"/> that StrafingStage resolves by running the real
    /// shooting chain with the carrying weapon.
    ///
    /// <para><b>Weapon-scoped</b>, which is what the whole corpus says: all 12 references sit on bomb
    /// weapons, and the attack is made *with* that weapon, so the rule cannot be modelled at unit scope
    /// without inventing a hit count. Its 12 references were dead as a scope mismatch until this slice.
    /// The "may only be used in this way" clause is enforced by <see cref="StrafingRules"/>, which keeps
    /// the weapon out of the shooting and melee pools — a live restriction, since every corpus bomb has
    /// range 0 and would otherwise be swung as a melee weapon.</para>
    ///
    /// <para><b>No fly-over permission.</b> The source rule grants none — it says "when this model moves
    /// through enemy units", presupposing a unit that already can. Every corpus carrier has Aircraft or
    /// Flying (the one footslogger, Saurian's Gecko Champion, gets Flying from the same Pterodactyl item
    /// that grants the bomb), and both emit <see cref="Effect.IgnoreEnemyMovementBlock"/> at unit scope.
    /// A weapon-scoped passive would not be read by <c>MovementRuleQueries.CanMoveThroughEnemies</c>
    /// anyway. StrafingStage warns once if a bearer turns up without the capability, since the ability
    /// could then never trigger.</para>
    ///
    /// <para>Like Martial Prowess, the attack operation is applied stage-side rather than through the
    /// <c>IOperationServices</c> seam: the hit/save/wound resolution is a child-stage pipeline, and the
    /// engine's fire-and-forget stage transitions only sequence correctly when that pipeline runs as a real
    /// child of the movement stage (the way Impact's hits run as a child of the melee stage) — not when
    /// driven from a service call that would return before the AssignWounds request is answered.</para>
    /// </summary>
    public static SpecialRuleDefinition Strafing { get; } = new SpecialRuleDefinition("Strafing",
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Movement_OnMoveThroughEnemy, new Cost.OncePerActivation(),
                new TargetSelector(1f, 1, 1, ETargetAffinity.Foe, false),
                new Effect.AttackWithThisWeapon(),
                new Condition.Always()),
        },
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "Once per activation, when moving through enemy units, pick one and attack it with " +
            "this weapon as if shooting. This weapon may only be used in this way.");

    /// <summary>
    /// Crossing Attack(X) (#197 P10): the auto-wound sibling of Strafing. When this unit moves through an
    /// enemy unit, once per activation it may pick that enemy and roll X dice - each 6+ deals one DIRECT
    /// unsaveable wound (Regeneration/Tough still apply). An activated ability at
    /// <see cref="EHookID.Movement_OnMoveThroughEnemy"/> whose <see cref="Effect.DealAutoWounds"/> queues an
    /// <see cref="RuleOperation.InvokeDealAutoWounds"/> that CrossingAttackStage rolls and feeds into wound
    /// assignment, skipping the save. The fly-over passive (like Strafing's) lets the move-through that
    /// triggers it be legal in the first place.
    /// </summary>
    public static SpecialRuleDefinition CrossingAttack { get; } = new SpecialRuleDefinition("Crossing Attack",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveThroughEnemy,
                new Condition.Always(),
                new Effect.IgnoreEnemyMovementBlock(),
                ELifetime.ThisActivation),
        },
        new[]
        {
            new ActivatedAbility(EHookID.Movement_OnMoveThroughEnemy, new Cost.OncePerActivation(),
                new TargetSelector(1f, 1, 1, ETargetAffinity.Foe, false),
                new Effect.DealAutoWounds(new ValueSource.Arg(0), SuccessThreshold: 6),
                new Condition.Always()),
        },
        Valence: EValence.Positive,
        Description: "Once per activation, when moving through an enemy unit, rolls dice that deal automatic unsaveable wounds.");

    /// <summary>
    /// #197 P10 Storm of X: once per game, when activated before attacking, roll 3 dice - for each 2+ the
    /// player picks an enemy unit within 12in that takes 3 hits with the storm's rule. Offered in Choose
    /// Action and resolved by StormStage (decisive pool -> integer target picks -> looping hit batches). The
    /// four variants differ only by the payload rule / AP: Change=Shred, Lust=Surge, Plague=Bane, War=AP(1).
    /// </summary>
    private static SpecialRuleDefinition MakeStorm(string name, string[] withRules, int armorPenetration,
        string payloadDescription) =>
        new SpecialRuleDefinition(name,
            Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerGame(),
                    new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                    new Effect.StormOfHits(PoolDice: 3, SuccessThreshold: 2, HitsPerSuccess: 3,
                        WithRules: withRules, ArmorPenetration: armorPenetration, RangeInches: 12f),
                    new Condition.Always()),
            },
            Valence: EValence.Positive,
            Description: $"Once per game before attacking, roll 3 dice; each 2+ deals 3 hits with {payloadDescription} to an enemy within 12in.");

    public static SpecialRuleDefinition StormOfChange { get; } = MakeStorm("Storm of Change", new[] { "Shred" }, 0, "Shred");
    public static SpecialRuleDefinition StormOfLust { get; } = MakeStorm("Storm of Lust", new[] { "Surge" }, 0, "Surge");
    public static SpecialRuleDefinition StormOfPlague { get; } = MakeStorm("Storm of Plague", new[] { "Bane" }, 0, "Bane");
    public static SpecialRuleDefinition StormOfWar { get; } = MakeStorm("Storm of War", System.Array.Empty<string>(), 1, "AP(1)");

    /// <summary>
    /// Strider (#102): the unit ignores the difficult-terrain movement cap — it may cross Difficult terrain
    /// without being limited to <see cref="Utilities.GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES"/>.
    /// A passive at <see cref="EHookID.Movement_OnMoveThroughTerrain"/> emitting
    /// <see cref="Effect.IgnoreTerrainEffects"/>, read by <see cref="MovementRuleQueries.IgnoresDifficultTerrain"/>
    /// and threaded into the movement validators (engine + every move resolver). Mirrors Strafing's fly-over
    /// passive above. Scope note: this waives only the Difficult cap; Dangerous-terrain tests and the enemy
    /// move-through block are unaffected (Flying's "ignore all terrain + move through units" facet is #029,
    /// which can reuse the same effect once those consumers also honour it).
    /// </summary>
    public static SpecialRuleDefinition Strider { get; } = new SpecialRuleDefinition("Strider",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveThroughTerrain,
                new Condition.Always(),
                new Effect.IgnoreTerrainEffects(),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Ignores the movement penalty for Difficult terrain.");

    /// <summary>
    /// Flying (#029): the bearer ignores ALL terrain movement effects — the difficult-terrain cap, Dangerous-
    /// terrain wound rolls, and Impassible-terrain blocking — AND may move through enemy units (it still may
    /// not END a move stacked on an enemy). Two passives: an <see cref="Effect.IgnoreTerrainEffects"/> with the
    /// <see cref="ETerrainIgnoreScope.AllTerrain"/> scope at <see cref="EHookID.Movement_OnMoveThroughTerrain"/>
    /// (read by <see cref="MovementRuleQueries.IgnoresAllTerrain"/> and threaded as the difficult + impassible
    /// waivers + the Dangerous-stage skip), and an <see cref="Effect.IgnoreEnemyMovementBlock"/> at
    /// <see cref="EHookID.Movement_OnMoveThroughEnemy"/> (the same move-through-units permission Strafing uses).
    /// </summary>
    public static SpecialRuleDefinition Flying { get; } = new SpecialRuleDefinition("Flying",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveThroughTerrain,
                new Condition.Always(),
                new Effect.IgnoreTerrainEffects(ETerrainIgnoreScope.AllTerrain),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveThroughEnemy,
                new Condition.Always(),
                new Effect.IgnoreEnemyMovementBlock(),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Ignores all terrain when moving and may move through enemy units (but may not end on them).");

    /// <summary>
    /// Aircraft (#029): a fast flyer with strong defensive properties — fully implemented.
    /// <list type="bullet">
    /// <item>Defensive shooting: enemies targeting it get −12" range (Subject <see cref="Effect.RangeModifier"/>
    /// at <see cref="EHookID.Shooting_OnRangeCheck"/> — effectively immune to ≤12" weapons) and −1 to hit (Subject
    /// <see cref="Effect.RollModifier"/>(Hit) at <see cref="EHookID.Shooting_OnHitRollModifier"/>, like Stealth
    /// without the distance gate); ignores all terrain + moves through units (Flying's two ops).</item>
    /// <item>Stage gates (via <see cref="AircraftRules.IsAircraft"/>): can't seize/contest objectives; can't be
    /// charged / moved into base contact with; must deploy before all other units.</item>
    /// <item>Forced movement: may only Advance (this <see cref="Effect.RestrictActions"/> at
    /// <see cref="EHookID.Activation_OnActionChoice"/>), which DefinePathStage turns into a forced straight-line
    /// 30–36" move along the unit's fixed heading (held on the models' <c>IModel.Facing</c>, set once toward the
    /// table centre, never turned); if it flies off a table edge it leaves play and redeploys from an edge next round
    /// (<c>ForcedAircraftMove</c> + StartOfRoundExtraActionStage). Simplifications, see WorkItems/029: the heading
    /// auto-aims toward centre (no player heading pick) and the redeploy zone is the whole table ("any edge"
    /// relaxed).</item>
    /// </list>
    /// </summary>
    public static SpecialRuleDefinition Aircraft { get; } = new SpecialRuleDefinition("Aircraft",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnRangeCheck,
                new Condition.AllModelsHaveThisRule(),
                new Effect.RangeModifier(Delta: -12),
                ELifetime.ThisActivation,
                ERuleSeat.Subject),
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.AllModelsHaveThisRule(),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
            new HookEntry(EHookID.Movement_OnMoveThroughTerrain,
                new Condition.Always(),
                new Effect.IgnoreTerrainEffects(ETerrainIgnoreScope.AllTerrain),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveThroughEnemy,
                new Condition.Always(),
                new Effect.IgnoreEnemyMovementBlock(),
                ELifetime.ThisActivation),
            // #029 forced movement: an Aircraft may only Advance (no Rush/Charge/Hold) — DefinePathStage turns
            // that Advance into a forced straight-line 30-36" move along its heading (see ForcedAircraftMove).
            new HookEntry(EHookID.Activation_OnActionChoice,
                new Condition.Always(),
                new Effect.RestrictActions(new[] { EActionType.Advance }),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Neutral,
        Description: "A fast flyer: enemies get -12\" range and -1 to hit it, and it ignores terrain and units; but it can't seize objectives or be charged, and must fly straight each turn.");

    /// <summary>
    /// Increased Shooting Range (#102): the bearer's own ranged weapons get +6" range. An Actor-seat passive
    /// at <see cref="EHookID.Shooting_OnRangeCheck"/> emitting <see cref="Effect.RangeModifier"/>(+6), folded
    /// by <see cref="RangeRuleQueries.EffectiveRange"/> and read by ChooseRangedAttackStage's target-eligibility
    /// check (engine-authoritative; the ChooseRangedAttack resolvers display it via the in-range model set).
    /// </summary>
    public static SpecialRuleDefinition IncreasedShootingRange { get; } = new SpecialRuleDefinition("Increased Shooting Range",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnRangeCheck,
                new Condition.Always(),
                new Effect.RangeModifier(Delta: +6),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "This unit's ranged weapons get +6\" range.");

    /// <summary>
    /// Ranged Shrouding (#102): enemies get −6" range when shooting this unit, to a minimum effective range of
    /// 6". A Subject-seat passive at <see cref="EHookID.Shooting_OnRangeCheck"/> emitting
    /// <see cref="Effect.RangeModifier"/>(−6, floor 6) — the defender contributes the debuff, mirroring how
    /// <see cref="Shielded"/> contributes a Subject-seat save bonus. Scope: the corpus's "where ALL MODELS have
    /// this rule" is enforced by <see cref="Condition.AllModelsHaveThisRule"/> (#183) — a joined hero that lacks
    /// it breaks the debuff for the unit. The floor adopts the "to a min. of 6\"" reading; some armies print
    /// Ranged Shrouding without it, but the two differ only for a weapon whose post-reduction range would fall
    /// below 6" (i.e. base range &lt; 12") — negligible for normal weapons.
    /// </summary>
    public static SpecialRuleDefinition RangedShrouding { get; } = new SpecialRuleDefinition("Ranged Shrouding",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnRangeCheck,
                new Condition.AllModelsHaveThisRule(),
                new Effect.RangeModifier(Delta: -6, MinResultInches: 6),
                ELifetime.ThisActivation,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Enemies get -6\" range when shooting this unit, to a minimum of 6\".");

    /// <summary>
    /// Darkborn (Offensive) (#102): the bearer gets +3" range when shooting AND moves +3" when charging. Two
    /// Actor-seat passives — a <see cref="Effect.RangeModifier"/>(+3) at <see cref="EHookID.Shooting_OnRangeCheck"/>,
    /// and a <see cref="Effect.MovementBonus"/>(Charge, +3) at <see cref="EHookID.Movement_OnMoveActionDeclared"/>
    /// gated to the Charge action (the same live move-distance seam Fast/Agile/RapidCharge use). The corpus uses
    /// the bare name "Darkborn" for two different rules across armies; this is the own-buff variant, named
    /// "Darkborn (Offensive)" to disambiguate from <see cref="DarkbornDefensive"/>.
    /// </summary>
    public static SpecialRuleDefinition DarkbornOffensive { get; } = new SpecialRuleDefinition("Darkborn (Offensive)",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnRangeCheck,
                new Condition.Always(),
                new Effect.RangeModifier(Delta: +3),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 3f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When shooting, +3\" range; when charging, +3\" movement.");

    /// <summary>
    /// Darkborn (Defensive) (#102/#029): enemies get −4" range when shooting this unit (to a min. of 6") AND
    /// −2" movement when charging it (to a min. of 6"). Two Subject-seat passives mirroring the Shrouding pair —
    /// a <see cref="Effect.RangeModifier"/>(−4, floor 6) at <see cref="EHookID.Shooting_OnRangeCheck"/> (read by
    /// <see cref="RangeRuleQueries.EffectiveRange"/>) and a <see cref="Effect.MovementBonus"/>(Charge, −2, floor 6)
    /// at <see cref="EHookID.Movement_OnChargeDeclared"/> (read by
    /// <see cref="MovementRuleQueries.EffectiveChargeDistanceAgainst"/> and applied as the worst-case charge-budget
    /// reduction in DefinePathStage). The corpus's other "Darkborn"; named to disambiguate from
    /// <see cref="DarkbornOffensive"/>. Scope: "where all models have this rule" is enforced by
    /// <see cref="Condition.AllModelsHaveThisRule"/> on both facets (#183).
    /// </summary>
    public static SpecialRuleDefinition DarkbornDefensive { get; } = new SpecialRuleDefinition("Darkborn (Defensive)",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnRangeCheck,
                new Condition.AllModelsHaveThisRule(),
                new Effect.RangeModifier(Delta: -4, MinResultInches: 6),
                ELifetime.ThisActivation,
                ERuleSeat.Subject),
            new HookEntry(EHookID.Movement_OnChargeDeclared,
                new Condition.AllModelsHaveThisRule(),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: -2f, MinResultInches: 6f),
                ELifetime.ThisActivation,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Enemies get -4\" range when shooting this unit and -2\" movement when charging it (each to a minimum of 6\").");

    /// <summary>
    /// Melee Shrouding (#029): enemies get −3" movement when charging this unit, to a minimum charge distance
    /// of 6" — the charge twin of <see cref="RangedShrouding"/>. A Subject-seat
    /// <see cref="Effect.MovementBonus"/>(Charge, −3, floor 6) at <see cref="EHookID.Movement_OnChargeDeclared"/>
    /// (previously a dormant hook), read by <see cref="MovementRuleQueries.EffectiveChargeDistanceAgainst"/> and
    /// applied as a worst-case reduction to the charger's budget in <c>DefinePathStage</c>. Scope: the corpus's
    /// "where ALL MODELS have this rule" is enforced by <see cref="Condition.AllModelsHaveThisRule"/> (#183), and
    /// the reduction is conservative (worst case among reachable enemies) because a charge's target isn't pinned
    /// until the move ends. Same mechanism unblocks defensive Darkborn's "−2 charge" and Aircraft-style facets.
    /// </summary>
    public static SpecialRuleDefinition MeleeShrouding { get; } = new SpecialRuleDefinition("Melee Shrouding",
        new[]
        {
            new HookEntry(EHookID.Movement_OnChargeDeclared,
                new Condition.AllModelsHaveThisRule(),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: -3f, MinResultInches: 6f),
                ELifetime.ThisActivation,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Enemies get -3\" movement when charging this unit, to a minimum of 6\".");

    // Unpredictable family (#197 P15): a per-attack-action randomized branch ---------------------------

    public const string UnpredictableRuleName = "Unpredictable";
    public const string UnpredictableFighterRuleName = "Unpredictable Fighter";
    public const string UnpredictableShooterRuleName = "Unpredictable Shooter";

    // The two arms every Unpredictable variant shares: +1 to hit on the HitBonus branch (folded at the
    // shared hit-roll-modifier hook), and AP(+1) - modelled as a -1 save modifier carried to the save stage,
    // same as Thrust - on the ApBonus branch. Both gate on the branch that UnpredictableBranchResolver rolled
    // once for this attack action (carried on the context via IHasUnpredictableBranch), so exactly one fires.
    private static HookEntry UnpredictableHitArm(Condition gate) =>
        new HookEntry(EHookID.Shooting_OnHitRollModifier,
            new Condition.And(gate, new Condition.UnpredictableBranchIs(EUnpredictableBranch.HitBonus)),
            new Effect.RollModifier(ERollKind.Hit, Delta: +1),
            ELifetime.ThisAttack);

    private static HookEntry UnpredictableApArm(Condition gate) =>
        new HookEntry(EHookID.Shooting_OnHitRollComplete,
            new Condition.And(gate, new Condition.UnpredictableBranchIs(EUnpredictableBranch.ApBonus)),
            new Effect.RollModifier(ERollKind.Save, Delta: -1),
            ELifetime.ThisAttack);

    /// <summary>
    /// Unpredictable (#197 P15): "when attacking, roll one die and apply one effect to all models with this
    /// rule: on a 1-3 AP(+1), on a 4-6 +1 to hit instead." The die is DECISIVE and rolled once per attack
    /// ACTION (<see cref="UnpredictableBranchResolver"/>, called from <c>CombatActionContext</c>), then
    /// carried down to the hit/save contexts so both arms read the SAME branch. Applies to both combat kinds,
    /// so neither arm carries an IsMelee gate.
    /// </summary>
    public static SpecialRuleDefinition Unpredictable { get; } = new SpecialRuleDefinition(UnpredictableRuleName,
        new[] { UnpredictableHitArm(new Condition.Always()), UnpredictableApArm(new Condition.Always()) },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When attacking, roll one die: on a 1-3 the unit gets AP(+1); on a 4-6 it gets +1 to hit instead.");

    /// <summary>
    /// Unpredictable Fighter (#197 P15): the melee-only <see cref="Unpredictable"/> - both arms gate on
    /// <see cref="Condition.IsMelee"/>. Same shared per-action decisive roll.
    /// </summary>
    public static SpecialRuleDefinition UnpredictableFighter { get; } = new SpecialRuleDefinition(UnpredictableFighterRuleName,
        new[] { UnpredictableHitArm(new Condition.IsMelee()), UnpredictableApArm(new Condition.IsMelee()) },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When in melee, roll one die: on a 1-3 AP(+1); on a 4-6 +1 to hit instead.");

    /// <summary>
    /// Unpredictable Shooter (#197 P15): the shooting-only <see cref="Unpredictable"/> - both arms gate on
    /// <c>Not(IsMelee)</c>. Same shared per-action decisive roll.
    /// </summary>
    public static SpecialRuleDefinition UnpredictableShooter { get; } = new SpecialRuleDefinition(UnpredictableShooterRuleName,
        new[]
        {
            UnpredictableHitArm(new Condition.Not(new Condition.IsMelee())),
            UnpredictableApArm(new Condition.Not(new Condition.IsMelee())),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "When shooting, roll one die: on a 1-3 AP(+1); on a 4-6 +1 to hit instead.");

    public static SpecialRuleDefinition UnpredictableFighterAura { get; } =
        UnitAura("Unpredictable Fighter Aura", UnpredictableFighterRuleName);

    public static SpecialRuleDefinition UnpredictableShooterAura { get; } =
        UnitAura("Unpredictable Shooter Aura", UnpredictableShooterRuleName);

    // Before-attack activated abilities (ChooseActionStage offers them at Activation_OnBeforeAttackAction
    // as menu actions; BeforeAttackActionStage resolves the chosen one) ---------------------------------

    /// <summary>
    /// Furious Buff (#100 #2e): once per activation, before attacking, the bearer picks one friendly unit
    /// within 12" which gains Furious once (next time it would apply). The grant rides
    /// <see cref="Effect.AddRule"/> with <see cref="ELifetime.NextTrigger"/>, so the granted Furious is read
    /// back by the evaluator and consumed the moment it fires (slices 1 + 2c). A representative "X Buff" rule.
    /// </summary>
    public static SpecialRuleDefinition FuriousBuff { get; } = new SpecialRuleDefinition("Furious Buff",
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.AddRule("Furious", ELifetime.NextTrigger),
                new Condition.Always()),
        },
        Valence: EValence.Positive,
        Description: "Once per activation, before attacking, give a friendly unit within 12\" Furious for its next attack.");

    /// <summary>
    /// Mend (#100 #2e): once per activation, before attacking, the bearer picks one friendly unit within 3"
    /// and removes D3 wounds from it (applied to that unit's first model — the per-model "with Tough"
    /// selection is approximated at unit scope for now). Exercises the previously-dead <see cref="Effect.Heal"/>
    /// consumer wired in <see cref="OperationApplier"/>.
    /// </summary>
    public static SpecialRuleDefinition Mend { get; } = new SpecialRuleDefinition("Mend",
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                new TargetSelector(3f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.Heal(new DiceExpression.D3()),
                new Condition.Always()),
        },
        Valence: EValence.Positive,
        Description: "Once per activation, before attacking, heal D3 wounds from a friendly unit within 3\".");

    // Action-restriction (ChooseActionStage drops the disallowed menu options) -------------------------

    /// <summary>
    /// Immobile (#100): the unit can't move — it may only Hold (and shoot). A passive rule at
    /// <see cref="EHookID.Activation_OnActionChoice"/> that queues
    /// <see cref="RuleOperation.RestrictActions"/> with the allowed set [Hold]; ChooseActionStage reads it
    /// and grays out Move and Charge. The general action-restriction primitive (also Artillery's deferred
    /// Hold-only facet).
    /// </summary>
    public static SpecialRuleDefinition Immobile { get; } = new SpecialRuleDefinition("Immobile",
        new[]
        {
            new HookEntry(EHookID.Activation_OnActionChoice,
                new Condition.Always(),
                new Effect.RestrictActions(new[] { EActionType.Hold }),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "This unit can't move - it may only Hold (and shoot).");
}
