using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Unit-selection dispatch for the Tactician (#191 A5). Spell target picks route to the
    /// planner's value-maximizing choice - a null reply there is a DELIBERATE cancel, legal only
    /// once the spell's minimum target count is met (see
    /// <see cref="TacticianPlanner.TryChooseSpellTarget"/>). Deploy-order picks hold
    /// matchup-sensitive units back (#191 A5-9, Chris's option 2): generalists deploy early,
    /// counters deploy late when more of the enemy layout is visible. #197 Surprise Attack's
    /// first-activation burst picks the enemy its hit pool is expected to hurt most. Every other unit
    /// selection (embark picks, ...) is the unmodified solo resolver (G3 fallback).
    /// </summary>
    public class TacticianUnitSelectionResolver
        : IStageResolver<SelectionRequest<UnitData>, DataBinding<UnitData>>
    {
        // CastSpellStage.PickTargets' instructions: "Choose target for {spell} ({k} of up to {N})".
        public const string SpellTargetInstructionPrefix = "Choose target for ";
        // ChooseUnitToDeployStage's literal instructions - the deploy-order discriminator.
        public const string DeployOrderInstructions = "Choose Unit to Deploy";

        private readonly TacticianPlanner _planner;
        private readonly IStageResolver<SelectionRequest<UnitData>, DataBinding<UnitData>> _soloFallback;
        private readonly ITableState? _tableState;
        private readonly RuleEvaluator? _evaluator;

        public TacticianUnitSelectionResolver(TacticianPlanner planner,
            IStageResolver<SelectionRequest<UnitData>, DataBinding<UnitData>> soloFallback,
            ITableState? tableState = null, RuleEvaluator? evaluator = null)
        {
            _planner = planner;
            _soloFallback = soloFallback;
            _tableState = tableState;
            _evaluator = evaluator;
        }

        public Task<DataBinding<UnitData>> Resolve(SelectionRequest<UnitData> request)
        {
            if (request.Instructions != null
                && request.Instructions.StartsWith(SpellTargetInstructionPrefix, StringComparison.Ordinal)
                && _planner.TryChooseSpellTarget(request.Instructions, request.ValidOptions,
                    out DataBinding<UnitData>? choice))
            {
                return Task.FromResult(choice!);
            }

            // #197 Surprise Attack: the mandatory first-activation burst target, priced by expected
            // damage. Same discriminator shape as the spell branch above, keyed on the stage's own
            // instruction constant rather than a duplicated literal.
            if (request.Instructions != null
                && request.Instructions.StartsWith(Stages.SurpriseAttackStage.PICK_INSTRUCTION_PREFIX,
                    StringComparison.Ordinal)
                && _planner.TryChooseBurstTarget(request.Instructions, request.ValidOptions,
                    out DataBinding<UnitData>? burstTarget))
            {
                return Task.FromResult(burstTarget!);
            }

            if (request.Instructions == DeployOrderInstructions
                && _tableState != null && _evaluator != null && request.ValidOptions.Count > 1)
            {
                // Lowest matchup sensitivity first; stable on ties (list order), so armies
                // without meaningful spread keep the solo bot's front-of-list order.
                DataBinding<UnitData> pick = request.ValidOptions[0].Option;
                float bestSensitivity = float.MaxValue;
                foreach (SelectionRequest<UnitData>.ValidOption option in request.ValidOptions)
                {
                    float sensitivity = DeploymentMatchup.Sensitivity(_evaluator, _tableState, option.Option);
                    if (sensitivity < bestSensitivity - 0.0001f)
                    {
                        bestSensitivity = sensitivity;
                        pick = option.Option;
                    }
                }
                return Task.FromResult(pick);
            }

            return _soloFallback.Resolve(request);
        }
    }
}
