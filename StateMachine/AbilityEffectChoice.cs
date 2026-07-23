using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// The shared "pick one effect" resolution for a rule that offers SEVERAL activated abilities at one
    /// hook (#197 P5a's labelled-ability shape: "when this unit is deployed or activated, pick one effect:
    /// either ... or ..."). Groups a hook's offers by rule, asks which effect when a rule offers more than
    /// one, then resolves / applies / executes the chosen ability.
    ///
    /// <para>Two stages resolve choices through here. <c>ActivationStartStage</c> (Versatile Attack,
    /// Versatile Reach, Watchborn, Versatile Defense) treats every group as a mandatory pick — the corpus
    /// says "pick one effect", not "you may". <c>DeployUnitStage</c> keeps its own Yes/No prompt for the
    /// single-ability offers whose text IS optional (Vanguard's reposition move, Fanatic's placement) and
    /// routes only the multi-ability groups here.</para>
    ///
    /// Logging stays with the caller: the two stages word their lines differently and their tests pin those
    /// strings.
    /// </summary>
    public static class AbilityEffectChoice
    {
        /// <summary>What <see cref="Resolve"/> did: which ability the player ended up with, and the
        /// operations it produced — returned so a caller can fold the stage-specific ones (a
        /// <see cref="RuleOperation.RepositionModels"/> through <see cref="RepositionPlacement"/>).</summary>
        public readonly record struct Outcome(AbilityOffer Chosen, IReadOnlyList<RuleOperation> Operations);

        /// <summary>
        /// A hook's offers grouped by the rule that offers them, in declaration order, so the option indices
        /// line up with the rule's authored ability list. One group == one "pick one effect" decision; a
        /// group of one is a rule with nothing to choose between.
        /// </summary>
        public static List<IReadOnlyList<AbilityOffer>> GroupByRule(IEnumerable<AbilityOffer> offers) =>
            offers.GroupBy(offer => offer.RuleName)
                .Select(group => (IReadOnlyList<AbilityOffer>)group.ToList())
                .ToList();

        /// <summary>
        /// Resolves one rule's group: asks which effect (only when there is a choice to make), then resolves
        /// the ability against <paramref name="unit"/>, applies its token operations and runs its executable
        /// ones. Self-targeted by construction — every corpus effect at these hooks buffs the bearer — so no
        /// target selection happens here.
        ///
        /// <paramref name="occasion"/> completes the prompt ("Havoc Brothers: pick one effect &lt;occasion&gt;.")
        /// — the caller's hook is what makes it accurate, since the same rule is asked at both deployment and
        /// activation start.
        /// </summary>
        public static async Task<Outcome> Resolve(IGameContext gameContext, PlayerID player, IUnit unit,
            IReadOnlyList<AbilityOffer> options, string occasion)
        {
            AbilityOffer chosen = options.Count == 1
                ? options[0]
                : options[await AskWhichEffect(gameContext, player, options[0].RuleName, unit, options,
                    occasion)];

            IReadOnlyList<RuleOperation> operations =
                gameContext.RuleEvaluator.ResolveAbility(chosen, new List<IUnit> { unit });

            OperationApplier.ApplyTokenOperations(operations);
            // Every ability-offering stage runs the executor too; without it an ability whose effect is
            // imperative (a triggered move, a fatigue) would be silently dropped.
            await OperationExecutor.Execute(operations, new GameOperationServices(gameContext));

            return new Outcome(chosen, operations);
        }

        private static async Task<int> AskWhichEffect(IGameContext gameContext, PlayerID player,
            string ruleName, IUnit unit, IReadOnlyList<AbilityOffer> options, string occasion)
        {
            List<ChooseAbilityEffectRequest.EffectOption> effectOptions = options
                .Select(offer => new ChooseAbilityEffectRequest.EffectOption(
                    LabelFor(offer), DescriptionFor(offer)))
                .ToList();

            var request = new ChooseAbilityEffectRequest(player, $"{unit.Name}: pick one effect {occasion}.",
                ruleName, unit.Name, effectOptions);

            int index = await gameContext.PlayerRequester
                .RequestDecision<ChooseAbilityEffectRequest, int>(request);

            // A resolver that answers out of range would silently mis-apply an effect; clamp to the first
            // option (what every resolver's own default already is) rather than throw mid-stage.
            if (index < 0 || index >= options.Count)
            {
                RuleDiagnostics.Warn($"'{ruleName}' effect choice returned index {index}, outside " +
                    $"0..{options.Count - 1}; defaulting to the first effect.");
                return 0;
            }

            return index;
        }

        // An unlabelled ability in a multi-ability rule is an authoring bug the option list would otherwise
        // render as a row of blanks; fall back to a positional label so the choice stays usable.
        private static string LabelFor(AbilityOffer offer) =>
            string.IsNullOrWhiteSpace(offer.Ability.Label) ? offer.RuleName : offer.Ability.Label;

        private static string DescriptionFor(AbilityOffer offer) =>
            offer.Ability.Effect is Effect.AddRule addRule ? addRule.RuleName : string.Empty;
    }
}
