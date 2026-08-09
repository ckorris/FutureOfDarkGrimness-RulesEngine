using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// A player-triggered ability surfaced as available at a hook. Distinct from a
    /// <see cref="Definitions.RuleOperation"/> on purpose: an operation is a resolved,
    /// deterministic action the engine will execute, whereas an offer is a
    /// pre-decision request — nothing is chosen, no cost paid, no randomness rolled
    /// yet. <see cref="RuleEvaluator.GatherOffers"/> gathers offers so the engine can
    /// present them to the player; once accepted with targets,
    /// <see cref="RuleEvaluator.ResolveAbility"/> produces the operation queue.
    /// </summary>
    /// <param name="Bearer">The unit whose rule provides the ability.</param>
    /// <param name="RuleName">The rule the ability came from (display + identity).</param>
    /// <param name="Ability">The full ability — Cost, TargetSelector, Effect, TriggerHook.</param>
    /// <param name="Arguments">The bearing rule's per-instance arguments (Crossing Attack's <c>(1)</c>), so
    /// an ability whose effect reads <see cref="ValueSource.Arg"/> resolves against the real value. Null
    /// (the default) means none — every argument-less ability, and any granted ability, which cannot carry
    /// arguments.</param>
    /// <param name="Weapon">The WEAPON whose rules offered this ability (#197 Strafing), or null when the
    /// offer came from the unit, a model, or a token grant. Threaded into the <see cref="RuleInvocation"/>
    /// at resolution so an effect can act with the carrying weapon — Strafing's "attack it with THIS
    /// weapon" has no other way to know which of the bearer's weapons is speaking.</param>
    /// <param name="Definition">The rule definition <see cref="RuleName"/> resolved to — the same
    /// <see cref="ResolvedRule.Definition"/> the gather walked. Carried so a menu can show the rule's
    /// player-facing <see cref="SpecialRuleDefinition.Description"/> beside the offer (#370): the offer's
    /// name alone is what the action menu was listing, which tells a player nothing about what taking it
    /// does. Null only for an offer built by hand (tests), never for one <see cref="RuleEvaluator"/>
    /// gathered, so consumers degrade to "no description" rather than throwing.</param>
    public sealed record AbilityOffer(IUnit Bearer, string RuleName, ActivatedAbility Ability,
        IReadOnlyList<RuleArgument>? Arguments = null, IWeapon? Weapon = null,
        SpecialRuleDefinition? Definition = null)
    {
        /// <summary> The arguments, normalized to an empty list when none were supplied. </summary>
        public IReadOnlyList<RuleArgument> ResolvedArguments => Arguments ?? System.Array.Empty<RuleArgument>();
    }
}
