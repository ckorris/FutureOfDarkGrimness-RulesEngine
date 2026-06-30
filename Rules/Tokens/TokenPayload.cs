using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

/// <summary>
/// Optional structured data attached to a <see cref="Token"/> when its
/// <see cref="Foundation.TokenType"/> and count alone don't carry enough information.
///
/// The common cases that motivate the type:
/// <list type="bullet">
///   <item>Spell-applied "next time it would apply" rule grants — the token says
///         "next time you shoot / are shot at, you have rule X" and needs to record
///         <i>which</i> rule. Without a payload you'd need a separate
///         <see cref="Foundation.TokenType"/> per grantable rule.</item>
///   <item>Cross-unit target-tagging tokens (e.g. <i>Unstoppable Mark</i>,
///         <i>Precision Fighting Mark</i>) that grant a rule to friendlies attacking
///         the bearer — payload carries the granted rule reference and any
///         affinity restriction.</item>
///   <item>One-shot stat modifiers granted by spells or abilities that don't have a
///         named rule equivalent (e.g. "+1 to hit shooting next attack") — payload
///         carries the roll kind and delta directly.</item>
///   <item>Targeted "only against X" buffs (e.g. <i>Raiding Drugs</i>) — payload
///         carries the granted rule plus a constrained target <see cref="UnitID"/>.</item>
/// </list>
///
/// Most tokens — Shaken, Fatigued, spell tokens, stacking markers — have no payload
/// and leave the field null. Implemented as a closed sum type via sealed nested
/// record subtypes (one per payload shape), so each concrete payload is type-checked
/// at the point of use rather than relying on stringly-typed dictionaries.
///
/// Subtypes are added on demand as the rules requiring them come up in test cases.
/// </summary>
public abstract record TokenPayload
{
    /// <summary>
    /// Records that the token grants the rule named <see cref="RuleName"/> to its
    /// bearer for the duration of <see cref="Lifetime"/>. Backs <c>Effect.AddRule</c>
    /// and <c>Effect.Aura</c> resolution — a "next time you shoot you have Blessed
    /// Ammo" buff, or an aura granting Regeneration to a whole unit, becomes a token
    /// carrying this payload rather than a bespoke token type per grantable rule.
    /// </summary>
    public sealed record RuleGrant(string RuleName, ELifetime Lifetime) : TokenPayload;

    /// <summary>
    /// Records a one-shot/duration numeric roll modifier granted to the bearer (#033 stat-modifier
    /// primitive) — a "+1 to hit / -1 to defense / -1 to morale" buff or debuff. The roll it applies to is
    /// the carrying token's <see cref="Foundation.TokenType"/> (HitRollModifier / SaveRollModifier /
    /// MoraleRollModifier), so this payload carries only the signed <see cref="Delta"/> — keeping
    /// <c>Tokens</c> independent of <c>ERollKind</c> (which lives in <c>Definitions</c>).
    /// </summary>
    public sealed record StatModifier(int Delta) : TokenPayload;

    /// <summary>
    /// #032 Limited: names the once-per-game weapon a <see cref="Foundation.TokenType.LimitedSpent"/> token
    /// records as fired. Lets a single model track two different Limited weapons independently (the spent-state
    /// lives on the model, keyed by weapon name since <c>Weapon</c> has no ID).
    /// </summary>
    public sealed record WeaponName(string Name) : TokenPayload;
}
