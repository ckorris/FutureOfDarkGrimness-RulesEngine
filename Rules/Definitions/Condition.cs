using System.Linq;
using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Predicate gating whether an <see cref="Effect"/> fires at a hook. Stored on
/// a <c>HookEntry</c>; evaluated by the engine against an <c>IHookContext</c>
/// when the relevant <see cref="EHookID"/> dispatches.
///
/// Implemented as an abstract record with sealed nested record subtypes — same
/// closed-sum-type pattern as <see cref="TokenClearTrigger"/> and <see cref="Cost"/>.
/// Pattern-match in a switch expression to evaluate:
///
/// <code>
/// bool ok = condition switch
/// {
///     Condition.Always                              => true,
///     Condition.UnmodifiedRollEquals(var v)         => ctx.UnmodifiedRoll == v,
///     Condition.DistanceGreaterThan(var n)          => ctx.Distance > n,
///     Condition.And(var l, var r)                   => Evaluate(l, ctx) && Evaluate(r, ctx),
///     // ...
/// };
/// </code>
///
/// Vocabulary grows on demand as the rule corpus surfaces new patterns. Start
/// with the half-dozen subtypes the first tests exercise; add the rest when a
/// real rule needs them.
/// </summary>
public abstract record Condition
{

    public virtual bool Evaluate(IHookContext context)
    {
        throw new NotImplementedException("Condition is not yet evaluable.");
    }
    
    public virtual IReadOnlyCollection<Type> RequiredCapabilities => Array.Empty<Type>();

    /// <summary>
    /// Always matches. Used for unconditional effects (e.g. passive aura
    /// modifiers that don't gate on any runtime state).
    /// </summary>
    public sealed record Always : Condition
    {
        public override bool Evaluate(IHookContext context) => true;
    }

    /// <summary>
    /// True if the unit the rule is attached to has another named rule.
    /// Used for unit-composition gates like Harassing Boost ("if this unit has
    /// Harassing, apply X").
    /// </summary>
    public sealed record UnitHasRule(string RuleName) : Condition;

    /// <summary>
    /// True if the target (defender, charged unit, spell target, etc.) has the
    /// named rule. Used for "vs. units with rule X" effects.
    /// </summary>
    public sealed record TargetHasRule(string RuleName) : Condition;

    /// <summary>
    /// True if the action the bearer just declared matches the given type.
    /// E.g. Rapid Rush applies only when the action is <see cref="EActionType.Rush"/>.
    /// </summary>
    public sealed record ActionTypeIs(EActionType ActionType) : CapabilityCondition<IHasActionType>
    {
        protected override bool EvaluateCore(IHasActionType context) => context.ActionType == ActionType;
    }

    /// <summary>
    /// True if the relevant die came up exactly this value before any modifiers
    /// were applied. Typically <see cref="DieValue"/> is 6 (Furious / Rending /
    /// Surge fire on natural 6) or 1 (Shred fires on natural 1 to block).
    /// </summary>
    public sealed record UnmodifiedRollEquals(int DieValue) : CapabilityCondition<IHasUnmodifiedHitRolls>
    {
        protected override bool EvaluateCore(IHasUnmodifiedHitRolls context)
        {
            return context.UnmodifiedHitRolls.At(DieValue) > 0;
        }
    }

    /// <summary>
    /// True if the source-to-target distance exceeds the given inches.
    /// E.g. Stealth applies when shot from &gt; 9" away; Piercing Hunter
    /// at &gt; 9".
    /// </summary>
    public sealed record DistanceGreaterThan(float DistanceInches) : CapabilityCondition<IHasDistance>
    {
        protected override bool EvaluateCore(IHasDistance context)
        {
            return context.DistanceInches > DistanceInches;
        }
    }

    /// <summary>
    /// True if the target's value for the given stat is at least the threshold.
    /// Two parameters because both <i>which stat</i> (Quality / Defense / Tough)
    /// and <i>the threshold</i> vary per rule — e.g. Melee Slayer keys on
    /// <see cref="EStatKind.Tough"/> &gt;= 3.
    /// </summary>
    public sealed record StatGreaterOrEqualTo(EStatKind Stat, int StatValue) : Condition;

    /// <summary>
    /// True if a majority of models in the target have Tough at least
    /// <see cref="MinToughValue"/>. Separate subtype because the "majority"
    /// computation isn't expressible as a simple stat comparison —
    /// it's a per-target structural query.
    /// </summary>
    public sealed record TargetMajorityHasTough(int MinToughValue) : Condition;

    /// <summary>
    /// True if the bearer's token container holds at least <see cref="MinCount"/>
    /// tokens of <see cref="TType"/>. Default <see cref="MinCount"/> = 1 makes
    /// the "do they have any?" case ergonomic; values &gt; 1 support stacking-
    /// marker thresholds (e.g. "do they have 2+ Piercing Frenzy markers?").
    /// </summary>
    public sealed record TokenPresent(TokenType TType, int MinCount = 1) : Condition;

    /// <summary>
    /// Logical AND of two conditions. Both must match.
    /// </summary>
    public sealed record And(Condition Left, Condition Right) : Condition
    {
        public override bool Evaluate(IHookContext context) =>
            Left.Evaluate(context) && Right.Evaluate(context);

        public override IReadOnlyCollection<Type> RequiredCapabilities =>
            Left.RequiredCapabilities.Concat(Right.RequiredCapabilities).Distinct().ToArray();
    }

    /// <summary>
    /// Logical OR of two conditions. Either matching is sufficient.
    /// </summary>
    public sealed record Or(Condition Left, Condition Right) : Condition;

    /// <summary>
    /// Logical NOT — true when the inner condition is false. Used for
    /// "not fatigued," "not in cover," etc.
    /// </summary>
    public sealed record Not(Condition Inner) : Condition
    {
        public override bool Evaluate(IHookContext context) => !Inner.Evaluate(context);

        public override IReadOnlyCollection<Type> RequiredCapabilities => Inner.RequiredCapabilities;
    }

    /// <summary>
    /// True if the attacking unit moved before this attack. Used by Indirect
    /// (-1 to hit when shooting after moving).
    /// </summary>
    public sealed record AfterMoving : CapabilityCondition<IHasAttackerMoved>
    {
        protected override bool EvaluateCore(IHasAttackerMoved context) => context.AttackerMoved;
    }
}
