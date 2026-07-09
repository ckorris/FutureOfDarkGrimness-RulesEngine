using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.StageResolution.Requests;

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
    /// activation ends. The stage therefore executes only token operations; it never runs a child pipeline,
    /// which is why it can be a leaf stage rather than a ParentStage like <c>PreAttackStage</c>.
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

            // Grouped by rule and kept in declaration order, so "pick one effect" means one pick per rule and
            // the option indices line up with the rule's authored ability list.
            List<IGrouping<string, AbilityOffer>> offersByRule = GameContext.RuleEvaluator
                .GatherOffers(new ActivationStartContext(unit))
                .GroupBy(offer => offer.RuleName)
                .ToList();

            foreach (IGrouping<string, AbilityOffer> ruleOffers in offersByRule)
            {
                List<AbilityOffer> options = ruleOffers.ToList();

                AbilityOffer chosen = options.Count == 1
                    ? options[0]
                    : options[await AskWhichEffect(context, ruleOffers.Key, unit, options)];

                // Self-targeted by construction (the corpus' activation-start effects all buff the bearer),
                // so no target selection: the bearer is the target.
                IReadOnlyList<RuleOperation> operations =
                    GameContext.RuleEvaluator.ResolveAbility(chosen, new List<IUnit> { unit });
                OperationApplier.ApplyTokenOperations(operations);

                GameContext.Log(options.Count == 1
                    ? $"{unit.Name}: {chosen.RuleName} applies."
                    : $"{unit.Name}: {chosen.RuleName} - chose {chosen.Ability.Label}.");
            }

            await OnFinished.Activate(context);
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
