using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// #442 (#429 Spell Drain): the before-attack ability's target pick (<c>BeforeAttackActionStage</c>'s
    /// "Choose target (k of up to N)", a <see cref="CancellableSelectionRequest{T}"/>) when the planner chose the
    /// ability: the enemy it aimed at, or - if the engine's eligibility differs from the planner's mirror - the
    /// offered enemy with the most spell tokens expected to drain. Never Cancel: backing out of the first pick
    /// aborts the ability unspent and loops Choose Action (the planner's attempt latch would stop a re-pick, but
    /// the activation would lose the ability for nothing). Any pick the planner did not ask for - the solo
    /// fallback's - goes to the solo resolver unchanged.
    /// </summary>
    public class TacticianBeforeAttackTargetResolver
        : IStageResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>
    {
        private readonly TacticianPlanner _planner;
        private readonly IStageResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>> _soloFallback;

        public TacticianBeforeAttackTargetResolver(TacticianPlanner planner,
            IStageResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>> soloFallback)
        {
            _planner = planner;
            _soloFallback = soloFallback;
        }

        public Task<CancellableResult<DataBinding<UnitData>>> Resolve(CancellableSelectionRequest<UnitData> request)
        {
            DataBinding<UnitData>? aimed = _planner.TakePendingDrainTarget();
            if (aimed == null || request.ValidOptions.Count == 0) return _soloFallback.Resolve(request);

            DataBinding<UnitData>? pick = request.ValidOptions
                .Select(o => o.Option)
                .FirstOrDefault(o => o.Reference.Equals(aimed.Reference));
            if (pick == null)
            {
                float best = float.NegativeInfinity;
                foreach (CancellableSelectionRequest<UnitData>.ValidOption option in request.ValidOptions)
                {
                    float value = _planner.ExpectedDrain(option.Option);
                    if (value > best) { best = value; pick = option.Option; }
                }
            }

            return Task.FromResult<CancellableResult<DataBinding<UnitData>>>(
                new Selected<DataBinding<UnitData>>(pick!));
        }
    }
}
