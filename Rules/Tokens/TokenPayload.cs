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

}
