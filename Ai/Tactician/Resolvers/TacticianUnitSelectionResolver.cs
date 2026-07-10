using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Unit-selection dispatch for the Tactician (#191 A5). Spell target picks route to the
    /// planner's value-maximizing choice - a null reply there is a DELIBERATE cancel, legal only
    /// once the spell's minimum target count is met (see
    /// <see cref="TacticianPlanner.TryChooseSpellTarget"/>). Every other unit selection (deploy
    /// order, embark picks, ...) is the unmodified solo resolver (G3 fallback).
    /// </summary>
    public class TacticianUnitSelectionResolver
        : IStageResolver<SelectionRequest<UnitData>, DataBinding<UnitData>>
    {
        // CastSpellStage.PickTargets' instructions: "Choose target for {spell} ({k} of up to {N})".
        public const string SpellTargetInstructionPrefix = "Choose target for ";

        private readonly TacticianPlanner _planner;
        private readonly IStageResolver<SelectionRequest<UnitData>, DataBinding<UnitData>> _soloFallback;

        public TacticianUnitSelectionResolver(TacticianPlanner planner,
            IStageResolver<SelectionRequest<UnitData>, DataBinding<UnitData>> soloFallback)
        {
            _planner = planner;
            _soloFallback = soloFallback;
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
            return _soloFallback.Resolve(request);
        }
    }
}
