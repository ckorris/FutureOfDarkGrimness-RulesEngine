using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
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
    /// A rule contributes one <see cref="ActivatedAbility"/> per effect, all at this hook, each labelled;
    /// <see cref="AbilityEffectChoice"/> groups the offers by rule and, when a rule offers more than one, asks
    /// the player which via <see cref="ChooseAbilityEffectRequest"/> — a mandatory pick, since the rule text
    /// says "pick one effect", not "you may pick". A rule offering exactly one ability applies it without a
    /// prompt: there is nothing to choose. The once-per-activation <see cref="Cost"/> is keyed on the rule
    /// name, so taking one effect spends the gate for its siblings.
    ///
    /// Each chosen ability's effect is an <see cref="Effect.AddRule"/> granting a helper rule, usually with
    /// <see cref="Rules.Foundation.ELifetime.ThisActivation"/> so it expires when the activation ends. The
    /// exception is Versatile Defense, whose text says "deployed OR activated ... lasts until the units' next
    /// activation": it is offered here AND at <c>DeployUnitStage</c>, and grants
    /// <see cref="Rules.Foundation.ELifetime.UntilNextActivation"/> — which is why this stage also opens by
    /// sweeping the previous grant (see <see cref="ClearUntilNextActivationTokens"/>).
    ///
    /// It also resolves the hook's PASSIVE entries — token grants, executables, and the
    /// reposition-at-activation placement (Wolfborn / Bounding / Rapid Blink), whose operations it sums into a
    /// single prompt. It never runs a child pipeline, which is why it can be a leaf stage rather than a
    /// ParentStage like <c>BeforeAttackActionStage</c>.
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

            ClearUntilNextActivationTokens(unit);

            await ResolvePassiveEntries(context, unit);

            // Grouped by rule and kept in declaration order, so "pick one effect" means one pick per rule and
            // the option indices line up with the rule's authored ability list.
            foreach (IReadOnlyList<AbilityOffer> ruleOffers in AbilityEffectChoice.GroupByRule(
                         GameContext.RuleEvaluator.GatherOffers(new ActivationStartContext(unit))))
            {
                // A rule offering exactly ONE ability at this hook is a "you MAY use" rule (Speed Feat's
                // once-per-game boost): offer it with a Yes/No, don't force it - declining saves it for a
                // later activation. The "pick one effect" rules (Versatile Attack/Defense/Reach, Watchborn)
                // all offer 2+ abilities and stay a mandatory pick. Mirrors DeployUnitStage's shape.
                if (ruleOffers.Count == 1)
                {
                    AbilityOffer offer = ruleOffers[0];
                    var question = new YesNoRequest(context.ActivatingPlayer(),
                        $"Use {offer.RuleName} on {unit.Name}?", defaultAnswer: true);
                    bool accepted = await GameContext.PlayerRequester
                        .RequestDecision<YesNoRequest, bool>(question);
                    if (!accepted) continue;
                }

                // #248: resolving the ability spends its cost and grants a rule — re-running this stage
                // would double-apply, so the activation can no longer be backed out of. Only reached once
                // an optional ability is accepted (declining above leaves the activation pristine).
                context.MarkIrreversibleAction();

                AbilityEffectChoice.Outcome outcome = await AbilityEffectChoice.Resolve(
                    GameContext, context.ActivatingPlayer(), unit, ruleOffers, "for this activation");

                GameContext.Log(ruleOffers.Count == 1
                    ? $"{unit.Name}: {outcome.Chosen.RuleName} applies."
                    : $"{unit.Name}: {outcome.Chosen.RuleName} - chose {outcome.Chosen.Ability.Label}.");
            }

            await OnFinished.Activate(context);
        }

        /// <summary>
        /// Retires the <see cref="Rules.Foundation.ELifetime.UntilNextActivation"/> grants this unit is
        /// carrying — the buff a previous deployment or activation chose, which was live through the
        /// opponent's turns and dies exactly here (Versatile Defense). Runs BEFORE the offers are gathered
        /// and before the passive entries, so the unit is clean when it re-picks: without it, choosing the
        /// other effect this activation would leave the unit holding both.
        ///
        /// Only this unit's containers are swept, which is what "the unit's next activation" means —
        /// another unit's grant is none of this activation's business. Mirrors
        /// <c>ReconcileEndOfActivationStage</c>, the same sweep at the other end of the activation.
        /// </summary>
        private void ClearUntilNextActivationTokens(IUnit unit)
        {
            List<ITokenContainer> containers = new List<ITokenContainer> { unit.Tokens };
            containers.AddRange(unit.Models.Select(model => model.Tokens));
            new TokenClearService().ClearForHook(EHookID.Activation_OnActivationStart, containers);
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

    }
}
