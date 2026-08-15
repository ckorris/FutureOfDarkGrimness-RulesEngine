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
    /// first-activation burst picks the enemy its hit pool is expected to hurt most. Deploy-time
    /// embark picks (#191 A5-10) load the tightest-fitting transport, refining the solo bot's
    /// first-offer accept with this profile's richer drop-off plan (A5-5 arrival timing, M12
    /// DeliverCargo, #355 disembark-to-charge). Every other unit selection is the unmodified solo
    /// resolver (G3 fallback).
    /// </summary>
    public class TacticianUnitSelectionResolver
        : IStageResolver<SelectionRequest<UnitData>, DataBinding<UnitData>>
    {
        // CastSpellStage.PickTargets' instructions: "Choose target for {spell} ({k} of up to {N})".
        public const string SpellTargetInstructionPrefix = "Choose target for ";
        // The deploy-order discriminator - now the stage's own constant (#191 A5-10 promoted it).
        public const string DeployOrderInstructions = Stages.ChooseUnitToDeployStage.CHOOSE_UNIT_INSTRUCTIONS;

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
                // A5-10: transports deploy before anything that could ride them - the embark offer
                // only exists for a transport ALREADY on the table, so a hold that deploys late
                // means cargo that walked. Within each group (transports, then the rest) the A5-9
                // order stands: lowest matchup sensitivity first; stable on ties (list order), so
                // armies without meaningful spread keep the solo bot's front-of-list order.
                DataBinding<UnitData> pick = request.ValidOptions[0].Option;
                bool bestIsTransport = false;
                float bestSensitivity = float.MaxValue;
                foreach (SelectionRequest<UnitData>.ValidOption option in request.ValidOptions)
                {
                    bool isTransport = TransportUtilities.IsTransport(option.Option.GetValue(), _evaluator);
                    float sensitivity = DeploymentMatchup.Sensitivity(_evaluator, _tableState, option.Option);
                    bool better = isTransport != bestIsTransport
                        ? isTransport
                        : sensitivity < bestSensitivity - 0.0001f;
                    if (better)
                    {
                        bestIsTransport = isTransport;
                        bestSensitivity = sensitivity;
                        pick = option.Option;
                    }
                }
                return Task.FromResult(pick);
            }

            // #191 A5-10 (owner's reversal of the #335 decline, 2026-08-15): ride whenever the
            // engine offers a hold. All profiles embark at deploy time now - the solo resolver
            // takes the FIRST offer (and is the fallthrough when this resolver is built without a
            // table state); the Tactician improves on it below with the tightest fit: the least
            // remaining capacity among the offers (every offer is engine-validated to fit this
            // unit already), so a small squad does not squat in a big hold that a later, bigger
            // squad needs. Ties keep list order. Keyed on the DEPLOY_NORMALLY_CHOICE label - the
            // one cancellable UnitData selection that carries it.
            if (request.AllowCancel
                && request.CancelLabel == Stages.ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE
                && _tableState != null && _evaluator != null && request.ValidOptions.Count > 0)
            {
                List<IUnit> allUnits = _tableState.Units.Objects.ToList();
                DataBinding<UnitData> pick = request.ValidOptions[0].Option;
                int tightest = int.MaxValue;
                foreach (SelectionRequest<UnitData>.ValidOption option in request.ValidOptions)
                {
                    int remaining = TransportUtilities.GetRemainingCapacity(
                        option.Option.GetValue(), allUnits, _evaluator);
                    if (remaining < tightest)
                    {
                        tightest = remaining;
                        pick = option.Option;
                    }
                }
                return Task.FromResult(pick);
            }

            return _soloFallback.Resolve(request);
        }
    }
}
