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
        Blast, Takedown, Impact, Counter, MartialProwess, Strafing, Fear, Fearless, Hero, Transport,
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
        Resistance, Protected, PiercingAssault, PiercingHunter,
        ResistanceAura, ProtectedAura, PiercingAssaultAura, PiercingHunterAura,
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
            Array.Empty<ActivatedAbility>());

    // Hit-roll-modifier sink (DetermineHitRollStage) -----------------------

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
                new Condition.DistanceGreaterThan(9f),
                new Effect.RollModifier(ERollKind.Hit, Delta: -2),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>());

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
        ERuleScope.Weapon);

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
        ERuleScope.Weapon);

    /// <summary>
    /// Defensive: enemies take -1 to hit when attacking this unit, in melee or shooting. The Subject-seat
    /// mirror of <see cref="Precise"/>; no distance/combat-kind gate, so it raises the threshold on every
    /// attack against the bearer (the same <c>OnHitRollModifier</c> sink Stealth/Artillery use).
    /// </summary>
    public static SpecialRuleDefinition Evasive { get; } = new SpecialRuleDefinition("Evasive",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Always(),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary>
    /// Defensive: enemies take -1 to hit in melee when attacking this unit. As <see cref="Evasive"/> but
    /// gated to melee via <c>IsMelee</c> (the same combat-kind condition Thrust/Indirect read), so the
    /// penalty applies only to melee swings against the bearer, not to shooting.
    /// </summary>
    public static SpecialRuleDefinition MeleeEvasion { get; } = new SpecialRuleDefinition("Melee Evasion",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.IsMelee(),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary>
    /// Attacker: +1 to hit when attacking (melee or shooting) — lowers the bearer's hit threshold. The
    /// Actor-seat counterpart to <see cref="Evasive"/>, no gate.
    /// </summary>
    public static SpecialRuleDefinition Precise { get; } = new SpecialRuleDefinition("Precise",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Always(),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

    /// <summary> Advance +4" (action-specific rapid mover). </summary>
    public static SpecialRuleDefinition RapidAdvance { get; } = new SpecialRuleDefinition("Rapid Advance",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Rush +6". </summary>
    public static SpecialRuleDefinition RapidRush { get; } = new SpecialRuleDefinition("Rapid Rush",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Rush),
                new Effect.MovementBonus(EActionType.Rush, DistanceInches: 6f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Charge +4". </summary>
    public static SpecialRuleDefinition RapidCharge { get; } = new SpecialRuleDefinition("Rapid Charge",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>());

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
        ERuleScope.Weapon);

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
        Array.Empty<ActivatedAbility>());

    // Hit-multiplier sink (RollToHitStage, after injection) ----------------------

    /// <summary>
    /// Blast(X): each hit is multiplied by X (the rule's argument), capped at the target unit's
    /// model count, and the attack ignores the target's cover. The multiply folds at
    /// <see cref="EHookID.Shooting_OnHitRollComplete"/> AFTER the hit-injection rules ("after other
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
        ERuleScope.Weapon);

    // Targets-selected marker (BuildTargetListStage, shooting) -------------------

    /// <summary>
    /// Takedown: the shooter may pick a single model in the target unit and resolve the attack against
    /// just it ("a unit of [1]") — all wounds funnel to that model with no carry-over. A passive rule at
    /// <see cref="EHookID.Shooting_OnShootTargetsSelected"/> that queues
    /// <see cref="RuleOperation.TargetIndividualModel"/>; BuildTargetListStage reads it, asks the
    /// attacker to pick the model, and AssignWoundsStage confines the wounds. The attack also ignores
    /// intervening line of sight and the target's cover (the W9 facet), queuing
    /// <see cref="RuleOperation.IgnoreLineOfSight"/> / <see cref="RuleOperation.IgnoreCover"/> at
    /// <see cref="EHookID.Shooting_OnSaveRollModifier"/> — the same machinery Indirect/Blast use.
    /// </summary>
    public static SpecialRuleDefinition Takedown { get; } = new SpecialRuleDefinition("Takedown",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnShootTargetsSelected,
                new Condition.Always(),
                new Effect.TargetIndividualModel(),
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
        ERuleScope.Weapon);

    // Melee charge rules on the shared hit + save sinks --------------------------

    /// <summary>
    /// Thrust: when charging in melee, +1 to hit and AP(+1). Both ride existing sinks — the +1
    /// to hit folds through the hit-roll-modifier sink at <see cref="EHookID.Shooting_OnHitRollModifier"/>
    /// (shared shoot/melee hook), and AP(+1) is modelled as a -1 save modifier folded at
    /// <see cref="EHookID.Shooting_OnHitRollComplete"/> and carried to the save stage (same machinery
    /// as Rending). The melee + charging gate distinguishes the charger's swing from a strike-back.
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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        ERuleScope.Weapon);

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
                new Condition.Always(),
                new Effect.StrikeFirst(),
                ELifetime.ThisActivation,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
                new Condition.Always(),
                new Effect.Reroll(ERollKind.Morale, new RerollCondition.AllFailures()),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        ERuleScope.Weapon);

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
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon);

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
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon);

    /// <summary>
    /// Crack: an unmodified 6 to hit gives the attack AP(+2) — modelled as -2 to the defender's save,
    /// carried hit-roll-complete → save stage by the same machinery as <see cref="Rending"/>, at half the
    /// AP and without Rending's Regeneration-ignore.
    /// </summary>
    public static SpecialRuleDefinition Crack { get; } = new SpecialRuleDefinition("Crack",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.UnmodifiedRollEquals(6),
                new Effect.RollModifier(ERollKind.Save, Delta: -2),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon);

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
        ERuleScope.Weapon);

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
        ERuleScope.Weapon);

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
            ERuleScope.Weapon);

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
            ERuleScope.Weapon);

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
            ERuleScope.Weapon);

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
            ERuleScope.Weapon);

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
            ERuleScope.Weapon);

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
            ERuleScope.Weapon);

    /// <summary> Rending in melee only: AP-4 on an unmodified 6 + ignore Regeneration, when in melee. </summary>
    public static SpecialRuleDefinition RendingInMelee { get; } =
        new SpecialRuleDefinition("Rending in melee",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.And(new Condition.IsMelee(), new Condition.UnmodifiedRollEquals(6)),
                    new Effect.RollModifier(ERollKind.Save, Delta: -4),
                    ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.IsMelee(),
                    new Effect.IgnoreRule("Regeneration"),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon);

    // Defensive wound-ignore + AP corpus rules (reuse IgnoreWoundOnRoll / save-modifier primitives) ------

    /// <summary>
    /// Resistance: when this unit takes wounds, each is ignored on a die roll of 6+. Regeneration's
    /// wound-ignore at a higher threshold — <see cref="Effect.IgnoreWoundOnRoll"/>(6) on the defender
    /// (Subject) at <see cref="EHookID.Shooting_OnSaveRollComplete"/>.
    /// </summary>
    public static SpecialRuleDefinition Resistance { get; } = new SpecialRuleDefinition("Resistance",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreWoundOnRoll(MinRoll: 6),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>());

    /// <summary> Protected: mechanically identical to <see cref="Resistance"/> — each wound ignored on a 6+. </summary>
    public static SpecialRuleDefinition Protected { get; } = new SpecialRuleDefinition("Protected",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreWoundOnRoll(MinRoll: 6),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        ERuleScope.Weapon);

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
        Array.Empty<ActivatedAbility>());

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
        },
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        EngineArgumentCount: 1);

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
        });

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
        });

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
                new Condition.Always()),
        });

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
        Array.Empty<ActivatedAbility>());

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
                new Condition.Always()),
        });

    // Mid-move attack primitive (StrafingStage offer -> save+wound sub-pipeline) ---

    /// <summary>
    /// Strafing: when this unit moves through an enemy unit, once per activation it may make a mid-move
    /// attack against that enemy (3 hits). An activated ability offered at
    /// <see cref="EHookID.Movement_OnMoveThroughEnemy"/>; accepting it queues an
    /// <see cref="RuleOperation.InvokeDealHits"/> that StrafingStage reads to resolve the hits through the
    /// shared save+wound stages.
    ///
    /// Like Martial Prowess, the deal-hits operation is applied stage-side rather than through the
    /// <c>IOperationServices</c> seam: the save/wound resolution is a child-stage pipeline, and the engine's
    /// fire-and-forget stage transitions only sequence correctly when that pipeline runs as a real child of
    /// the movement stage (the way Impact's hits run as a child of the melee stage) — not when driven from a
    /// service call that would return before the AssignWounds request is answered.
    /// </summary>
    public static SpecialRuleDefinition Strafing { get; } = new SpecialRuleDefinition("Strafing",
        new[]
        {
            // The fly-over permission: a Strafing unit may path through enemy bases (it still may not end on
            // one). Read by MovementRuleQueries.CanMoveThroughEnemies; without it #011's validator would block
            // the very move-through that triggers the ability below.
            new HookEntry(EHookID.Movement_OnMoveThroughEnemy,
                new Condition.Always(),
                new Effect.IgnoreEnemyMovementBlock(),
                ELifetime.ThisActivation),
        },
        new[]
        {
            new ActivatedAbility(EHookID.Movement_OnMoveThroughEnemy, new Cost.OncePerActivation(),
                new TargetSelector(1f, 1, 1, ETargetAffinity.Foe, false),
                new Effect.DealHits(Count: 3, WithRules: Array.Empty<string>()),
                new Condition.Always()),
        });

    // Pre-attack activated abilities (PreAttackStage offers them at Activation_OnPreAttack) -------------

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
            new ActivatedAbility(EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.AddRule("Furious", ELifetime.NextTrigger),
                new Condition.Always()),
        });

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
            new ActivatedAbility(EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                new TargetSelector(3f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.Heal(new DiceExpression.D3()),
                new Condition.Always()),
        });

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
        Array.Empty<ActivatedAbility>());
}
