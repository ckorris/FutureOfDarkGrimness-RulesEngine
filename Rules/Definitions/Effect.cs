using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// What a rule does when its <see cref="Condition"/> matches at a hook.
/// Stored on a <c>HookEntry</c> (passive rules) or on an <c>ActivatedAbility</c>
/// (player-triggered abilities and spells). The engine translates a matched
/// effect into one or more <c>RuleOperation</c> items in the operation queue,
/// which it then applies to game state.
///
/// Closed sum type — abstract record with sealed nested record subtypes, same
/// pattern as <see cref="Condition"/>, <see cref="TokenClearTrigger"/>, and
/// <see cref="Cost"/>. Pattern-match in the effect dispatcher to translate
/// each subtype into the operations it queues.
///
/// Vocabulary grows on demand. Subtypes not exercised by current tests can be
/// left out of the initial commit and added when a rule first needs them.
/// </summary>
public abstract record Effect
{
    public virtual void Apply(IHookContext context, List<RuleOperation> operations)
    {
        throw new NotImplementedException("Effect is not yet applyable.");
    }
    
    public virtual IReadOnlyCollection<Type> RequiredCapabilities => Array.Empty<Type>();
    
    /// <summary>
    /// Modifies a base stat (Quality / Defense / Tough) by <see cref="Delta"/>
    /// for the duration given by <see cref="LifetimeScope"/>. Unlike
    /// <see cref="RollModifier"/>, this persists across multiple rolls — e.g.
    /// "+1 Defense until end of round" rather than "+1 to this one save roll."
    /// </summary>
    public sealed record StatModifier(EStatKind Stat, int Delta, ELifetime LifetimeScope) : Effect;

    /// <summary>
    /// Adds or subtracts <see cref="Delta"/> to a single dice roll of the given
    /// <see cref="RollKind"/>. The most common effect: Stealth -1 to hit,
    /// Courage +1 to morale, AP(X) -X to enemy defense, etc. Applies only to
    /// the one roll in flight at the firing hook.
    /// </summary>
    public sealed record RollModifier(ERollKind RollKind, int Delta) : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ApplyRollModifier(RollKind, Delta));
        }
    }

    /// <summary>
    /// Forces a reroll of dice in the current roll matching
    /// <see cref="Condition"/>. Covers Bane (reroll unmodified Defense 6s),
    /// Lacerate (reroll same on attacker side), Fearless (reroll failed morale
    /// on 4+).
    /// </summary>
    public sealed record Reroll(ERollKind Roll, RerollCondition Condition) : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ApplyReroll(Roll, Condition));
        }
    }

    /// <summary>
    /// Each die that came up unmodified <see cref="OnRollValue"/> generates
    /// <see cref="Count"/> additional hits (defaults to one). Covers Furious,
    /// Surge, Relentless — all the "natural 6 → bonus hit" rules.
    /// </summary>
    public sealed record AddExtraHit(int OnRollValue, int Count = 1) : CapabilityEffect<IHasUnmodifiedHitRolls>
    {
        protected override void ApplyCore(IHasUnmodifiedHitRolls context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InsertExtraHits(context.UnmodifiedHitRolls.At(OnRollValue) * Count));
        }
    }

    /// <summary>
    /// Same shape as <see cref="AddExtraHit"/> but for wound generation.
    /// Covers Shred (+1 wound on unmodified 1 to block).
    /// </summary>
    public sealed record AddExtraWound(int OnRollValue, int Count = 1) : Effect;

    /// <summary>
    /// Adjusts the bearer's movement distance by <see cref="DistanceInches"/>
    /// (positive or negative) when the action being taken is
    /// <see cref="ActionType"/>. Covers Fast (+2"/Advance, +4"/Rush+Charge),
    /// Slow, Rapid Rush (+6"/Rush), and target-perspective movement penalties
    /// like Melee Shrouding (-3" on enemy Charge).
    /// </summary>
    public sealed record MovementBonus(EActionType ActionType, float DistanceInches) : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ApplyMovementBonus(ActionType, DistanceInches));
        }
    }

    /// <summary>
    /// Suppresses another rule named <see cref="RuleName"/> in the current
    /// evaluation chain — removes its effects from the operation queue before
    /// the engine applies them. Covers Bane / Smash / Unstoppable (ignore
    /// Regeneration). This is the rule-vs-rule primitive that lets Plan B
    /// express "X counters Y" without engine code.
    /// </summary>
    public sealed record IgnoreRule(string RuleName) : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.SuppressRule(RuleName));
        }
    }

    /// <summary>
    /// Grants the bearer the rule named <see cref="RuleName"/> for the duration
    /// of <see cref="Scope"/>. Typical use: spell-applied "next time" buffs
    /// (Blessed Ammo, Fade in the Dark, Protective Dome) with
    /// <see cref="ELifetime.NextTrigger"/>; activation-scoped grants
    /// (Versatile Attack/Reach) with <see cref="ELifetime.ThisActivation"/>.
    /// Distinct from <see cref="Aura"/>, which grants to all unit-mates.
    /// </summary>
    public sealed record AddRule(string RuleName, ELifetime Scope) : Effect;

    /// <summary>
    /// Grants the rule named <see cref="RuleName"/> to every model in the
    /// bearer's unit for as long as the bearer remains alive. Distinct from
    /// <see cref="AddRule"/> because aura propagation is unit-wide and tied to
    /// bearer existence rather than a named lifetime. Covers Regeneration
    /// Aura, Furious Aura, Melee Shrouding Aura, etc.
    /// </summary>
    public sealed record Aura(string RuleName) : Effect;

    /// <summary>
    /// Inflicts <see cref="Count"/> hits on the target unit, with each hit
    /// carrying the additional rules named in <see cref="WithRules"/>
    /// (e.g. <c>AP</c>, <c>Blast</c>, <c>Lacerate</c>, <c>Deadly</c>).
    /// The universal offensive-spell shape: Cerebral Trauma
    /// (1 hit with Blast and Lacerate), Lightning Fog (4 hits), Psychic Terror
    /// (9 hits with Bane). <see cref="Count"/> is a fixed authored value
    /// because every offensive-spell hit-count we've seen in the corpus is
    /// fixed — no rule-data randomness on the count itself.
    /// </summary>
    public sealed record DealHits(int Count, IReadOnlyList<string> WithRules) : Effect;

    /// <summary>
    /// Removes <see cref="Amount"/> wounds from the target model. The only
    /// rule-data effect with a genuinely random amount — Mend uses D3 — hence
    /// the <see cref="DiceExpression"/> parameter rather than a fixed int.
    /// </summary>
    public sealed record Heal(DiceExpression Amount) : Effect;

    /// <summary>
    /// Adds <see cref="Count"/> tokens of <see cref="TType"/> to the bearer's
    /// container with the specified <see cref="Clear"/> policy. Covers cost-
    /// gate setup ("used-this-activation" markers cleared at
    /// <see cref="EHookID.Activation_OnEndOfActivation"/>), spell-token
    /// replenishment (Caster(X) grants <c>Arg(0)</c> at round start), marker
    /// placement (Piercing Frenzy grants one marker on enemy-destroyed), and
    /// cross-unit target tagging when paired with a target selector. The count is
    /// a <see cref="ValueSource"/> so fixed grants use <c>Literal</c> while
    /// arg-driven grants (Caster) use <c>Arg</c>.
    /// </summary>
    public sealed record GrantToken(TokenType TType, ValueSource Count, TokenClearTrigger Clear) : Effect;

    /// <summary>
    /// Removes <see cref="Count"/> tokens of <see cref="TType"/> from the
    /// bearer's container. Covers paying activated-ability costs — spending
    /// spell tokens, consuming once-per-game markers, etc. Pairs with
    /// <see cref="Cost.ConsumesToken"/> at the activated-ability layer.
    /// </summary>
    public sealed record ConsumeToken(TokenType TType, int Count) : Effect;

    /// <summary>
    /// Invokes the movement subsystem inline — the bearer moves up to
    /// <see cref="MaxInches"/>. If <see cref="IsOptional"/>, the player may
    /// decline the move. Engine primitive (Phase 7h / item #042 engine refactor)
    /// that rules invoke without re-implementing movement logic. Covers
    /// Harassing (3" after shooting/melee, optional), Re-Position Artillery,
    /// Vanguard reposition.
    /// </summary>
    public sealed record TriggeredMove(float MaxInches, bool IsOptional) : Effect;

    /// <summary>
    /// Triggers a second activation of the bearer this round. Engine primitive
    /// the Martial Prowess rule invokes. Currently no parameters because
    /// self-reactivation is the only case in the corpus; could grow a
    /// <c>UnitID Target</c> parameter if a future rule reactivates a different
    /// unit.
    /// </summary>
    public sealed record Reactivate : Effect;

    /// <summary>
    /// Multiplies each wound the attack deals by <see cref="Multiplier"/>. Covers
    /// Deadly(X) — the multiplier is the rule's argument, so it's a
    /// <see cref="ValueSource"/> (typically <c>Arg(0)</c>) rather than a literal.
    /// </summary>
    public sealed record MultiplyWounds(ValueSource Multiplier) : Effect;

    /// <summary>
    /// Caps the bearer's hit-roll target at <see cref="Quality"/> (a floor on the
    /// roll needed): the attack hits on <see cref="Quality"/>+ regardless of the
    /// model's own Quality. Covers Reliable ("Attacks at Quality 2+"). A fixed
    /// authored value, not a rule argument.
    /// </summary>
    public sealed record QualityFloor(int Quality) : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.QualityFloor(Quality));
        }
    }

    /// <summary>
    /// Each wound the bearer would take is ignored on an unmodified roll of
    /// <see cref="MinRoll"/> or higher. Covers Regeneration (5+). Fixed authored
    /// threshold, not a rule argument.
    /// </summary>
    public sealed record IgnoreWoundOnRoll(int MinRoll) : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreWound(MinRoll));
        }
    }

    /// <summary>
    /// Sets the bearer model's maximum wounds to <see cref="Amount"/> at creation.
    /// Covers Tough(X) — the value is the rule's argument (<c>Arg(0)</c>). Models
    /// default to 1 max wound; Tough raises it.
    /// </summary>
    public sealed record SetMaxWounds(ValueSource Amount) : Effect;

    /// <summary>
    /// Multiplies each hit the attack scores by <see cref="Multiplier"/> (capped at
    /// the target's model count by the engine). Covers Blast(X) — the multiplier is
    /// the rule's argument.
    /// </summary>
    public sealed record MultiplyHits(ValueSource Multiplier) : Effect;

    /// <summary>
    /// On a charge, rolls <see cref="DiceCount"/> impact dice (each 2+ a hit) before
    /// strikes. Covers Impact(X) — the dice count is the rule's argument.
    /// </summary>
    public sealed record ChargeImpactHits(ValueSource DiceCount) : Effect;

    /// <summary>
    /// Adds <see cref="Amount"/> to the bearer's wound tally when deciding who won a
    /// melee. Covers Fear(X) — the amount is the rule's argument.
    /// </summary>
    public sealed record ExtraMeleeWoundCount(ValueSource Amount) : Effect;

    /// <summary>
    /// The bearer strikes before the charging unit resolves its strikes. Covers
    /// Counter ("Strikes first with this weapon when charged"). The companion facet
    /// — the charger losing 1 Impact roll per Counter model — is a separate
    /// Impact-count modifier added when that interaction is modelled.
    /// </summary>
    public sealed record StrikeFirst : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.StrikeFirst());
        }
    }

    /// <summary>
    /// Resolves the attack against a single chosen model in the target unit, as if
    /// it were a unit of one. Covers Takedown. Structural targeting change with no
    /// numeric parameter; the "resolved first, before other weapons" ordering is a
    /// dispatch-time detail (Phase 8).
    /// </summary>
    public sealed record TargetIndividualModel : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.TargetIndividualModel());
        }
    }

    /// <summary>
    /// Restricts the bearer to declaring only the actions in <see cref="Allowed"/>.
    /// Covers Immobile (<c>[Hold]</c> only) and Artillery's Hold-only facet. The
    /// engine drops disallowed actions from the choice set.
    /// </summary>
    public sealed record RestrictActions(IReadOnlyList<EActionType> Allowed) : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.RestrictActions(Allowed));
        }
    }

    /// <summary>
    /// Adjusts the effective range of attacks made against the bearer by
    /// <see cref="Delta"/> inches. Covers Aircraft ("units targeting it get -12\"
    /// range"). Distinct from <see cref="RollModifier"/>, which adjusts the roll
    /// itself rather than the range threshold.
    /// </summary>
    public sealed record RangeModifier(int Delta) : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ApplyRangeModifier(Delta));
        }
    }

    /// <summary>
    /// The bearer ignores terrain effects while moving. Covers Strider (difficult
    /// terrain only) and Flying (all terrain effects). The Flying-only facet —
    /// moving through units as well — is a separate movement-permission flag added
    /// when that distinction is executed (Phase 8).
    /// </summary>
    public sealed record IgnoreTerrainEffects : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreTerrainEffects());
        }
    }

    /// <summary>
    /// Sets the bearer aside during normal deployment to be placed by its own
    /// dedicated stage. Covers Ambush (deploy a later round, &gt;9" from enemies)
    /// and Scout (deploy after others, within 12" of the zone). The timing and
    /// placement constraints that distinguish the two are execution details
    /// (Phase 8 / the #042 engine refactor).
    /// </summary>
    public sealed record DeferDeployment : Effect
    {
        public override void Apply(IHookContext context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.DeferDeployment());
        }
    }
}
