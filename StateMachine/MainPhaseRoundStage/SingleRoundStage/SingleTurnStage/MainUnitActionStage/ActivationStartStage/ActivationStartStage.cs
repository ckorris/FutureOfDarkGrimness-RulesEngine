using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P5a — fires the previously dormant <see cref="Rules.Foundation.EHookID.Activation_OnActivationStart"/>,
    /// the first thing that happens in a unit's activation, and resolves the "pick one effect until the end of
    /// the activation" rules the corpus hangs there (Versatile Attack, Watchborn, Versatile Reach).
    ///
    /// A rule contributes one <see cref="ActivatedAbility"/> per effect, all at this hook, each labelled. This
    /// stage groups the offers by rule and, when a rule offers more than one, asks the player which via
    /// <see cref="ChooseAbilityEffectRequest"/> — a mandatory pick, since the rule text says "pick one effect",
    /// not "you may pick". A rule offering exactly one ability applies it without a prompt: there is nothing
    /// to choose. The once-per-activation <see cref="Cost"/> is keyed on the rule name, so taking one effect
    /// spends the gate for its siblings.
    ///
    /// Each chosen ability's effect is an <see cref="Effect.AddRule"/> with
    /// <see cref="Rules.Foundation.ELifetime.ThisActivation"/>, granting a helper rule that expires when the
    /// activation ends.
    ///
    /// It also resolves the hook's PASSIVE entries — token grants, executables, and the
    /// reposition-at-activation placement (Wolfborn / Bounding / Rapid Blink), whose operations it sums into a
    /// single prompt. It never runs a child pipeline, which is why it can be a leaf stage rather than a
    /// ParentStage like <c>PreAttackStage</c>.
    ///
    /// Runs exactly once per activation: it is <c>MainUnitActionStage</c>'s starting child, and every loop
    /// back from Movement/Melee/Shoot returns to <c>ChooseActionStage</c>, not here.
    /// </summary>
    public class ActivationStartStage : StageBase<IUnitActionContext>
    {
        public StageBinding OnFinished;

        public ActivationStartStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            GameContext.LogDebug("Entered Activation Start.");

            IUnit unit = context.ActivatingUnit.GetValue();

            await ResolvePassiveEntries(context, unit);

            // Grouped by rule and kept in declaration order, so "pick one effect" means one pick per rule and
            // the option indices line up with the rule's authored ability list.
            List<IGrouping<string, AbilityOffer>> offersByRule = GameContext.RuleEvaluator
                .GatherOffers(new ActivationStartContext(unit))
                .GroupBy(offer => offer.RuleName)
                .ToList();

            foreach (IGrouping<string, AbilityOffer> ruleOffers in offersByRule)
            {
                List<AbilityOffer> options = ruleOffers.ToList();

                // #248: resolving the ability spends its once-per-activation cost and grants a
                // ThisActivation rule — re-running this stage would double-apply, so the activation can
                // no longer be backed out of.
                context.MarkIrreversibleAction();

                AbilityOffer chosen = options.Count == 1
                    ? options[0]
                    : options[await AskWhichEffect(context, ruleOffers.Key, unit, options)];

                // Self-targeted by construction (the corpus' activation-start effects all buff the bearer),
                // so no target selection: the bearer is the target.
                IReadOnlyList<RuleOperation> operations =
                    GameContext.RuleEvaluator.ResolveAbility(chosen, new List<IUnit> { unit });
                OperationApplier.ApplyTokenOperations(operations);
                // Every other ability-offering stage runs the executor too; without it an ability whose effect
                // is imperative (a triggered move, a fatigue) would be silently dropped here.
                await OperationExecutor.Execute(operations, new GameOperationServices(GameContext));

                GameContext.Log(options.Count == 1
                    ? $"{unit.Name}: {chosen.RuleName} applies."
                    : $"{unit.Name}: {chosen.RuleName} - chose {chosen.Ability.Label}.");
            }

            await OnFinished.Activate(context);
        }

        /// <summary>
        /// The PASSIVE half of this hook. Nothing evaluated passive entries at Activation_OnActivationStart
        /// before, so a rule that hung one there did nothing; the reposition-at-activation rules
        /// (Wolfborn / Bounding / Rapid Blink) are the first that need it.
        ///
        /// Token grants apply, executables run, and every <see cref="RuleOperation.RepositionModels"/> is
        /// SUMMED into a single placement — a unit with both Rapid Blink and Rapid Blink Boost gets one prompt
        /// for 2D3in, not two prompts for D3in each.
        /// </summary>
        private async Task ResolvePassiveEntries(IUnitActionContext context, IUnit unit)
        {
            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new ActivationStartContext(unit), RuleParticipant.Actor(unit));

            if (operations.Count == 0) return;

            // #248: token grants / executables / repositions applied here are irreversible — re-running
            // this stage on a re-activation would double-apply them, so back-out is off the table.
            context.MarkIrreversibleAction();

            OperationApplier.ApplyTokenOperations(operations);
            await OperationExecutor.Execute(operations, new GameOperationServices(GameContext));

            // "You MAY place all models anywhere fully within Nin of their position" - a placement, not a
            // move, folded into one prompt for the summed radius. Shared with DeployUnitStage (Fanatic).
            await RepositionPlacement.OfferFromOperations(GameContext, context.ActivatingUnit.GetValue(), operations);
        }

        private async Task<int> AskWhichEffect(IUnitActionContext context, string ruleName, IUnit unit,
            IReadOnlyList<AbilityOffer> options)
        {
            List<ChooseAbilityEffectRequest.EffectOption> effectOptions = options
                .Select(offer => new ChooseAbilityEffectRequest.EffectOption(
                    LabelFor(offer), DescriptionFor(offer)))
                .ToList();

            ChooseAbilityEffectRequest request = new ChooseAbilityEffectRequest(
                context.ActivatingPlayer(), $"{unit.Name}: pick one effect for this activation.",
                ruleName, unit.Name, effectOptions);

            int index = await GameContext.PlayerRequester
                .RequestDecision<ChooseAbilityEffectRequest, int>(request);

            // A resolver that answers out of range would silently mis-apply an effect; clamp to the first
            // option (what every resolver's own default already is) rather than throw mid-activation.
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
