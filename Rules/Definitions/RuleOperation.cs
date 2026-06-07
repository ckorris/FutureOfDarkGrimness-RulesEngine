using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using Microsoft.ApplicationInsights.Extensibility;

namespace FDG.Rules.Definitions;

/// <summary>
/// The output queue of effect evaluation — what the engine should concretely
/// do, after <see cref="Effect"/>s have been resolved against the live hook
/// context. Where an Effect declares "what the rule wants" (context-free,
/// potentially with random or expandable parameters), a RuleOperation describes
/// "what the engine should actually do" (deterministic, bound to specific
/// entities, randomness already rolled, auras already expanded).
///
/// Most Effects map 1:1 to a single RuleOperation; the split exists for the
/// cases that need resolution:
/// <list type="bullet">
///   <item><see cref="Effect.Heal"/> rolls its <see cref="DiceExpression"/> at
///         dispatch time; the resulting <see cref="InvokeHeal"/> carries the
///         concrete int.</item>
///   <item><see cref="Effect.AddExtraHit"/> expands to zero or more
///         <see cref="InsertExtraHits"/> based on how many natural rolls
///         actually matched.</item>
///   <item><see cref="Effect.Aura"/> expands into one
///         <see cref="GrantTokenToUnit"/> per model in the bearer's unit.</item>
///   <item>Engine-primitive invocations (<see cref="InvokeTriggeredMove"/>,
///         <see cref="InvokeReactivate"/>, <see cref="InvokeDealHits"/>) bind
///         to live entity references that aren't present in the Effect's
///         context-free declaration.</item>
///   <item><see cref="SuppressRule"/> operates on the resolved queue (removing
///         pending Operations originating from the named rule) — its
///         counterpart <see cref="Effect.IgnoreRule"/> couldn't do the same
///         job at the declaration level.</item>
/// </list>
///
/// The queue is pure data once produced — deterministic, serializable, and
/// inspectable. This is what lets rule tests assert on the returned queue
/// rather than mocking game state and observing side effects, and what makes
/// the queue a candidate snapshot/replay artifact in the future.
///
/// Implementation note: each Operation should carry an origin-rule tag at
/// dispatch time (sidecar metadata, not a field on the records) so
/// <see cref="SuppressRule"/> can identify which entries to remove. That's a
/// Phase 7 dispatcher detail, not a shape concern at this layer.
/// </summary>
public abstract record RuleOperation
{
    /// <summary>
    /// Human-readable description of what this operation does, for game-log output. The
    /// evaluator pairs it with the bearer + rule ("Goblins' Stealth added -1 to Hit rolls").
    /// Default is generic; operations override it where a specific phrasing reads better.
    /// </summary>

    public virtual string Describe()
    {
        return "applied an effect"; //Placeholder, should be overridden.
    }
    
    /// <summary>
    /// Adjust the modifier stack of the in-flight roll of <see cref="Roll"/>
    /// kind by <see cref="Delta"/>. Resolution of <see cref="Effect.RollModifier"/>.
    /// </summary>
    public sealed record ApplyRollModifier(ERollKind Roll, int Delta) : SinkOperation<IRollModifierSink>
    {
        public override void ApplyTo(IRollModifierSink sink)
        {
            sink.Add(Roll, Delta);
        }

        public override string Describe()
        {
            return $"added {(Delta >= 0 ? "+" : "")}{Delta} to {Roll} rolls";
        }
    }

    /// <summary>
    /// Re-roll dice in the current <see cref="Roll"/> matching
    /// <see cref="Condition"/>. Resolution of <see cref="Effect.Reroll"/>.
    /// </summary>
    public sealed record ApplyReroll(ERollKind Roll, RerollCondition Condition) : RuleOperation;

    /// <summary>
    /// Add <see cref="Count"/> extra hits to the current attack's hit pool.
    /// Resolution of <see cref="Effect.AddExtraHit"/> after the dispatcher has
    /// counted matching natural-roll dice (<c>results.At(value)</c>). Count is a
    /// float because that match count is fractional under the probabilistic
    /// roller (e.g. n dice yield n/6 natural 6s).
    /// </summary>
    public sealed record InsertExtraHits(float Count) : SinkOperation<IHitInjectionSink>
    {
        public override void ApplyTo(IHitInjectionSink sink)
        {
            sink.AddHits(Count);
        }

        public override string Describe()
        {
            return $"added {Count} extra hits";
        }
    }

    /// <summary>
    /// Add <see cref="Count"/> extra wounds to the current attack's wound
    /// total. Resolution of <see cref="Effect.AddExtraWound"/>. Count is a float
    /// for the same reason as <see cref="InsertExtraHits"/> — the matching-die
    /// count is fractional under the probabilistic roller.
    /// </summary>
    public sealed record InsertExtraWounds(float Count) : RuleOperation;

    /// <summary>
    /// Apply a persistent stat modifier of <see cref="Delta"/> to
    /// <see cref="Stat"/> for the duration of <see cref="LifetimeScope"/>.
    /// Resolution of <see cref="Effect.StatModifier"/>.
    /// </summary>
    public sealed record ApplyStatModifier(EStatKind Stat, int Delta, ELifetime LifetimeScope) : RuleOperation;

    /// <summary>
    /// Adjust the in-flight movement budget by <see cref="DistanceInches"/>
    /// when the declared action is <see cref="ActionType"/>. Resolution of
    /// <see cref="Effect.MovementBonus"/>.
    /// </summary>
    public sealed record ApplyMovementBonus(EActionType ActionType, float DistanceInches)
        : SinkOperation<IMovementModifierSink>
    {
        public override void ApplyTo(IMovementModifierSink sink)
        {
            sink.Add(ActionType, DistanceInches);
        }

        public override string Describe()
        {
            return $"added {(DistanceInches >= 0 ? "+" : "")}{DistanceInches}\" to {ActionType} movement";

        }
    }

    /// <summary>
    /// Walk the rest of the pending operation queue and remove any operations
    /// whose origin tag matches <see cref="RuleName"/>. Resolution of
    /// <see cref="Effect.IgnoreRule"/>. The mechanism that lets Bane / Smash /
    /// Unstoppable cancel Regeneration without engine code.
    /// </summary>
    public sealed record SuppressRule(string RuleName) : RuleOperation
    {
        public override string Describe()
        {
            return $"ignored {RuleName}";
        }
    }

    /// <summary>
    /// Add <see cref="TokenToGrant"/> to <see cref="Unit"/>'s token container.
    /// Resolution of <see cref="Effect.GrantToken"/> targeting a unit, and the
    /// per-unit-mate expansion of <see cref="Effect.Aura"/>.
    /// </summary>
    public sealed record GrantTokenToUnit(IUnit Unit, Token TokenToGrant) : RuleOperation;

    /// <summary>
    /// Add <see cref="TokenToGrant"/> to <see cref="Model"/>'s token container.
    /// Resolution of <see cref="Effect.GrantToken"/> targeting a model
    /// (e.g. model-scoped Regenerative Strength markers).
    /// </summary>
    public sealed record GrantTokenToModel(IModel Model, Token TokenToGrant) : RuleOperation;

    /// <summary>
    /// Remove up to <see cref="Count"/> tokens of <see cref="TType"/> from
    /// <see cref="Unit"/>'s container. Resolution of
    /// <see cref="Effect.ConsumeToken"/> targeting a unit, and of
    /// <see cref="Cost.ConsumesToken"/> at activated-ability cost-resolution.
    /// </summary>
    public sealed record ConsumeTokensFromUnit(IUnit Unit, TokenType TType, int Count) : RuleOperation;

    /// <summary>
    /// Remove up to <see cref="Count"/> tokens of <see cref="TType"/> from
    /// <see cref="Model"/>'s container. Resolution of
    /// <see cref="Effect.ConsumeToken"/> targeting a model.
    /// </summary>
    public sealed record ConsumeTokensFromModel(IModel Model, TokenType TType, int Count) : RuleOperation;

    /// <summary>
    /// Invoke the engine's movement subsystem for <see cref="Unit"/> with a
    /// budget of <see cref="MaxInches"/>, optionally declinable by the player
    /// if <see cref="IsOptional"/>. Resolution of
    /// <see cref="Effect.TriggeredMove"/>. Depends on the engine refactor that
    /// exposes movement as a callable subsystem (item #042 engine work).
    /// </summary>
    public sealed record InvokeTriggeredMove(IUnit Unit, float MaxInches, bool IsOptional) : RuleOperation;

    /// <summary>
    /// Re-enter the activation flow for <see cref="Unit"/> this round.
    /// Resolution of <see cref="Effect.Reactivate"/>. Depends on the engine
    /// exposing re-activation as a callable primitive.
    /// </summary>
    public sealed record InvokeReactivate(IUnit Unit) : RuleOperation;

    /// <summary>
    /// Invoke the wound-application flow on <see cref="Target"/> as if from a
    /// weapon with <see cref="Count"/> hits carrying the rules named in
    /// <see cref="WithRules"/>. Resolution of <see cref="Effect.DealHits"/>;
    /// the universal offensive-spell delivery mechanism.
    /// </summary>
    public sealed record InvokeDealHits(IUnit Target, int Count, IReadOnlyList<string> WithRules) : RuleOperation;

    /// <summary>
    /// Restore <see cref="Amount"/> wounds on <see cref="Target"/>. Resolution
    /// of <see cref="Effect.Heal"/>; the <see cref="DiceExpression"/> has already
    /// been rolled by the dispatcher, so <see cref="Amount"/> is concrete — a
    /// float, since a rolled heal is fractional under the probabilistic roller
    /// (a D6 heal averages 3.5).
    /// </summary>
    public sealed record InvokeHeal(IModel Target, float Amount) : RuleOperation;

    /// <summary>
    /// Multiply the in-flight attack's wound count by <see cref="Multiplier"/>.
    /// Resolution of <see cref="Effect.MultiplyWounds"/>; the rule's argument has
    /// already been read, so <see cref="Multiplier"/> is a concrete int. Folded by
    /// <c>WoundModifierSink</c> at wound-assignment time.
    /// </summary>
    public sealed record MultiplyWounds(int Multiplier) : SinkOperation<IWoundModifierSink>
    {
        public override void ApplyTo(IWoundModifierSink sink)
        {
            sink.Multiply(Multiplier);
        }

        public override string Describe()
        {
            return $"multiplied wounds by {Multiplier}";
        }
    }

    /// <summary>
    /// Cap the in-flight hit roll's target at <see cref="Quality"/>+. Resolution of
    /// <see cref="Effect.QualityFloor"/> (Reliable).
    /// </summary>
    public sealed record QualityFloor(int Quality) : RuleOperation;

    /// <summary>
    /// Ignore each wound on an unmodified roll of <see cref="MinRoll"/>+. Resolution
    /// of <see cref="Effect.IgnoreWoundOnRoll"/> (Regeneration). Suppressible by
    /// <see cref="SuppressRule"/> from Bane / Rending / Unstoppable.
    /// </summary>
    public sealed record IgnoreWound(int MinRoll) : RuleOperation;

    /// <summary>
    /// Set a model's maximum wounds to <see cref="MaxWounds"/>. Resolution of
    /// <see cref="Effect.SetMaxWounds"/> (Tough). Carries no model reference yet —
    /// per-model targeting lands with model-scoped rule attachment in Phase 7.
    /// </summary>
    public sealed record SetMaxWounds(int MaxWounds) : RuleOperation;

    /// <summary>
    /// Multiply the in-flight hit count by <see cref="Multiplier"/> (engine caps at
    /// target model count). Resolution of <see cref="Effect.MultiplyHits"/> (Blast).
    /// </summary>
    public sealed record MultiplyHits(int Multiplier) : RuleOperation;

    /// <summary>
    /// Roll <see cref="DiceCount"/> impact dice (each 2+ a hit) on the charge.
    /// Resolution of <see cref="Effect.ChargeImpactHits"/> (Impact).
    /// </summary>
    public sealed record ChargeImpactHits(int DiceCount) : RuleOperation;

    /// <summary>
    /// Add <see cref="Amount"/> to the bearer's melee-won wound tally. Resolution of
    /// <see cref="Effect.ExtraMeleeWoundCount"/> (Fear).
    /// </summary>
    public sealed record ExtraMeleeWoundCount(int Amount) : RuleOperation;

    /// <summary>
    /// Insert the bearer's strikes ahead of the charging unit's. Resolution of
    /// <see cref="Effect.StrikeFirst"/> (Counter).
    /// </summary>
    public sealed record StrikeFirst : RuleOperation;

    /// <summary>
    /// Re-scope the in-flight attack to a single chosen model in the target unit.
    /// Resolution of <see cref="Effect.TargetIndividualModel"/> (Takedown).
    /// </summary>
    public sealed record TargetIndividualModel : RuleOperation;

    /// <summary>
    /// Limit the bearer's declarable actions to <see cref="Allowed"/>. Resolution of
    /// <see cref="Effect.RestrictActions"/> (Immobile, Artillery Hold-only).
    /// </summary>
    public sealed record RestrictActions(IReadOnlyList<EActionType> Allowed) : RuleOperation;

    /// <summary>
    /// Adjust the effective range of attacks against the bearer by <see cref="Delta"/>
    /// inches. Resolution of <see cref="Effect.RangeModifier"/> (Aircraft).
    /// </summary>
    public sealed record ApplyRangeModifier(int Delta) : RuleOperation;

    /// <summary>
    /// Suppress terrain effects for the bearer's current move. Resolution of
    /// <see cref="Effect.IgnoreTerrainEffects"/> (Strider, Flying).
    /// </summary>
    public sealed record IgnoreTerrainEffects : RuleOperation;

    /// <summary>
    /// Remove the bearer from the normal deployment pool for later placement.
    /// Resolution of <see cref="Effect.DeferDeployment"/> (Ambush, Scout).
    /// </summary>
    public sealed record DeferDeployment : RuleOperation;
}

/// <summary>
/// A <see cref="RuleOperation"/> that applies itself to a sink of type <typeparamref name="TSink"/>.
/// The generic parameter declares which sink the operation targets — the operation-side mirror of
/// <c>CapabilityEffect&lt;TCap&gt;</c>. A stage folds a whole category of operations by pulling them with
/// <c>OfType&lt;SinkOperation&lt;TSink&gt;&gt;()</c> and calling <see cref="ApplyTo"/>; new operations of the
/// same sink category are caught automatically, with no concrete-type switch. Operations that target no sink
/// (e.g. <see cref="RuleOperation.SuppressRule"/>) stay plain <see cref="RuleOperation"/>.
/// </summary>

public abstract record SinkOperation<TSink> : RuleOperation
{
    public abstract void ApplyTo(TSink sink);
}
