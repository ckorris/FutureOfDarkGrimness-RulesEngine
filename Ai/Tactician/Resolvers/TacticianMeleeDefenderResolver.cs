using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Melee defender choice by CombatMath (#191 A4-3) instead of the solo bot's fewest-models
    /// count: pick the target with the best value-weighted exchange (our expected wounds into them,
    /// minus their return strikes into us), with a bonus for a kill.
    /// </summary>
    public class TacticianMeleeDefenderResolver
        : IStageResolver<ChooseMeleeDefenderRequest, CancellableResult<DataBinding<UnitData>>>
    {
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;
        private readonly TacticianPlanner _planner;

        public TacticianMeleeDefenderResolver(ITableState tableState, RuleEvaluator evaluator,
            TacticianPlanner planner)
        {
            _tableState = tableState;
            _evaluator = evaluator;
            _planner = planner;
        }

        public Task<CancellableResult<DataBinding<UnitData>>> Resolve(ChooseMeleeDefenderRequest request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException("AI received a melee defender request with no valid options.");

            DataBinding<UnitData>? attacker = _planner.ActiveUnit;
            DataBinding<UnitData> best = request.ValidOptions[0].Option;
            float bestScore = float.NegativeInfinity;

            foreach (CancellableSelectionRequest<UnitData>.ValidOption option in request.ValidOptions)
            {
                float score;
                if (attacker != null)
                {
                    UnitData target = option.Option.GetValue();
                    MeleeEstimate melee = CombatMath.EstimateMelee(_evaluator, attacker, option.Option);
                    float remaining = Math.Max(1f, target.RemainingWounds);
                    float dealtFraction = Math.Min(1f, melee.AttackerAttack.ExpectedWounds / remaining);
                    float takenFraction = Math.Min(1f, melee.DefenderReturn.ExpectedWounds
                        / Math.Max(1f, attacker.GetValue().RemainingWounds));
                    score = dealtFraction * TacticalAnalysis.UnitValue(target)
                            * (melee.AttackerAttack.DestroysUnit ? TacticianWeights.MeleeKillBonus : 1f)
                        - takenFraction * TacticalAnalysis.UnitValue(attacker.GetValue());
                }
                else
                {
                    // No known active unit (shouldn't happen mid-activation): weakest-first, solo-style.
                    score = -option.Option.GetValue().Models.Count(m => m.GetIsAlive());
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = option.Option;
                }
            }

            return Task.FromResult<CancellableResult<DataBinding<UnitData>>>(
                new Selected<DataBinding<UnitData>>(best));
        }
    }
}
