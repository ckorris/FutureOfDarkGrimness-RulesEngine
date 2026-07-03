namespace FDG.Rules.Dispatch;

/// <summary>
/// How a model-aware rule evaluation composes the per-model rules of the model(s) a hook involves (#093).
/// The engine's default is that special rules are unit-wide; a model may carry extra rules of its own
/// (a joined Hero today; champions/mixed units later), and different hooks combine those model rules
/// differently:
/// <list type="bullet">
///   <item><see cref="AnyOwner"/> — union. A rule fires if ANY involved model carries it (still deduped
///     per unit, so it fires once — the rulebook's "instances of the same rule don't stack"). The right
///     semantics when a rule belongs to individual models independently: a joined Caster's round-start
///     spell-token grant (one caster model in an otherwise non-caster unit) must still grant.</item>
///   <item><see cref="AllOwners"/> — intersection. A rule fires only if EVERY involved model carries it.
///     The right semantics for a pooled combat batch, where a rule modifies the whole indivisible batch
///     (Furious's "each unmodified 6 scores an extra hit" reads the batch's shared dice): applying it is
///     only unambiguous when all the batch's contributors share it, and doing so avoids leaking one
///     model's rule onto the others' pooled roll — and firing it once (not once per owner) keeps the
///     no-stack rule. Attribution of a mixed-rule same-weapon batch to individual models is a deferred
///     #093 corner.</item>
/// </list>
/// </summary>
public enum EModelRuleScope
{
    AnyOwner,
    AllOwners,
}
