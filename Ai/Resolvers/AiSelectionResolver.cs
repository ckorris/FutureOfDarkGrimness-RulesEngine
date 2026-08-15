using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Resolvers
{
    public class AiSelectionResolver<T> : IStageResolver<SelectionRequest<T>, DataBinding<T>>
    {
        // Optional: only the deploy-order transports-first bias needs it (IsTransport is a rule-graph
        // question). Built without one - the Tactician's embedded fallbacks, tests - every branch that
        // reads it is skipped and the resolver keeps its plain first-option behavior.
        private readonly RuleEvaluator? _evaluator;

        public AiSelectionResolver(RuleEvaluator? evaluator = null)
        {
            _evaluator = evaluator;
        }

        public Task<DataBinding<T>> Resolve(SelectionRequest<T> request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException($"AI received a {nameof(SelectionRequest<T>)} with no valid options.");

            // #191 A5-10 (owner's reversal of the #335 decline, 2026-08-15): "during deployment, it's
            // almost always best to put something in transports" - so the deploy-time embark prompt is
            // ANSWERED (first offered transport; every offer is engine-validated to fit) rather than
            // declined. The distinction that #335 was really after is deploy-time vs MID-GAME: embarking
            // after deployment stays off the menu (AiStringSelectionResolver filters Embark), and the
            // solo-grade disembark timing that makes the ride land lives in ShouldDisembark there.
            //
            // Matched on the shared DEPLOY_NORMALLY_CHOICE constant - no other cancellable selection
            // carries that label, so a melee-defender pick is untouched (cancelling one would loop).
            // The branch is now the same as the fallthrough, but stays written out so the decision
            // (and its history) is greppable at the decision point.
            if (request.AllowCancel
                && request.CancelLabel == ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE)
            {
                return Task.FromResult(request.ValidOptions[0].Option);
            }

            // #191 A5-10: transports deploy before anything that could ride them - the embark offer only
            // exists for a transport ALREADY on the table, so a hold that deploys late means cargo that
            // walked. First transport in list order wins; no transports (or no evaluator to ask) keeps
            // the plain front-of-list order.
            if (_evaluator != null
                && request.Instructions == ChooseUnitToDeployStage.CHOOSE_UNIT_INSTRUCTIONS)
            {
                foreach (SelectionRequest<T>.ValidOption option in request.ValidOptions)
                {
                    if (option.Option.GetValue() is IUnit unit
                        && TransportUtilities.IsTransport(unit, _evaluator))
                    {
                        return Task.FromResult(option.Option);
                    }
                }
            }

            return Task.FromResult(request.ValidOptions[0].Option);
        }
    }
}
