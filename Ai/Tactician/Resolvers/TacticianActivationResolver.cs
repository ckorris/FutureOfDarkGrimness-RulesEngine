using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Activation order by urgency (#191 A4-1) instead of the solo bot's first-in-list: activate
    /// the unit with the most to gain (value-weighted kill opportunity), the most to flip
    /// (objective within this activation's reach), or the most to lose (value-weighted incoming
    /// threat) - act before the opponent's next activation removes the unit or its opportunity.
    /// </summary>
    public class TacticianActivationResolver : IStageResolver<ChooseUnitToActivateRequest, DataBinding<UnitData>>
    {
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;
        private readonly TacticianPlanner? _planner;

        public TacticianActivationResolver(ITableState tableState, RuleEvaluator evaluator,
            TacticianPlanner? planner = null)
        {
            _tableState = tableState;
            _evaluator = evaluator;
            _planner = planner;
        }

        public Task<DataBinding<UnitData>> Resolve(ChooseUnitToActivateRequest request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException(
                    $"AI received a {nameof(ChooseUnitToActivateRequest)} with no valid options.");

            if (request.ValidOptions.Count == 1)
            {
                _planner?.BeginActivation(request.ValidOptions[0].Option);
                return Task.FromResult(request.ValidOptions[0].Option);
            }

            DataBinding<UnitData> best = request.ValidOptions[0].Option;
            float bestScore = float.NegativeInfinity;
            foreach (SelectionRequest<UnitData>.ValidOption option in request.ValidOptions)
            {
                float score = Urgency(option.Option);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = option.Option;
                }
            }
            _planner?.BeginActivation(best);
            return Task.FromResult(best);
        }

        /// <summary>
        /// The A4-1 urgency score. All terms are value-weighted fractions ("how much of that unit's
        /// worth changes hands"), so kill and threat compare on one scale; the flip term is a flat
        /// bonus because objectives decide games, not casualties.
        /// </summary>
        public float Urgency(DataBinding<UnitData> unitBinding)
        {
            UnitData unit = unitBinding.GetValue();
            Position here = Centroid(unit);
            float advance = TacticalAnalysis.AdvanceDistance(unit, _evaluator);
            float rush = TacticalAnalysis.RushDistance(unit, _evaluator);

            float kill = 0f;
            float threat = 0f;
            foreach (DataBinding<UnitData> enemyBinding in EnemyBindings(unit.PlayerID))
            {
                UnitData enemy = enemyBinding.GetValue();
                float distance = UnitCompareUtilities.MinDistanceBetweenUnits(unit, enemy,
                    out _, out _, includeVertical: true);

                // What could WE do to them this activation (shoot after advancing, at best range)?
                float reachAfterAdvance = Math.Max(1f, distance - advance);
                AttackEstimate ours = CombatMath.EstimateShooting(_evaluator, unitBinding, enemyBinding,
                    new AttackContext(reachAfterAdvance, AttackerMoved: true));
                kill = Math.Max(kill, ValueFraction(ours.ExpectedWounds, enemy));

                // What could THEY do to us from where things stand (their advance included)?
                float theirReach = Math.Max(1f, distance - TacticalAnalysis.AdvanceDistance(enemy, _evaluator));
                AttackEstimate theirs = CombatMath.EstimateShooting(_evaluator, enemyBinding, unitBinding,
                    new AttackContext(theirReach, AttackerMoved: true));
                threat = Math.Max(threat, ValueFraction(theirs.ExpectedWounds, unit));
            }

            float flip = 0f;
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                bool oursAlready = projection.ProjectedOwner.HasValue
                    && projection.ProjectedOwner.Value == unit.PlayerID;
                if (oursAlready) continue;
                float distanceTo = TacticalAnalysis.MinBaseEdgeDistanceToPoint(unit, projection.Objective.Position);
                if (distanceTo <= rush + TacticalAnalysis.ObjectiveSeizureRadiusInches)
                {
                    flip = 1f;
                    break;
                }
            }

            // A5-6 (Chris): boat-then-payload - a loaded transport acts early so it can drive
            // before the cargo's own activation decides whether to get out; embarked cargo acts
            // late (nothing it does matters until the boat has moved).
            float transportBias = 0f;
            if (TransportUtilities.IsEmbarked(unit))
                transportBias = TacticianWeights.ActivationEmbarkedCargoBias;
            else if (TransportUtilities.IsTransport(unit)
                && TransportUtilities.GetOccupants(unit, _tableState.Units.Objects.ToList()).Any())
                transportBias = TacticianWeights.ActivationLoadedTransportBias;

            return TacticianWeights.ActivationKillOpportunity * kill
                 + TacticianWeights.ActivationObjectiveFlip * flip
                 + TacticianWeights.ActivationUnderThreat * threat
                 + transportBias;
        }

        // Expected wounds as a fraction of the target's remaining wounds, weighted by its value -
        // "how much unit-worth this trade moves".
        private static float ValueFraction(float expectedWounds, UnitData target)
        {
            float remaining = Math.Max(1f, target.RemainingWounds);
            return Math.Min(1f, expectedWounds / remaining) * TacticalAnalysis.UnitValue(target) / 100f;
        }

        private IEnumerable<DataBinding<UnitData>> EnemyBindings(PlayerID us)
        {
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (army.PlayerID == us || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                    if (unit.GetValue().Models.Any(m => m.GetIsAlive())
                        && unit.GetValue().GetIsOnBattlefield())
                        yield return unit;
            }
        }

        private static Position Centroid(UnitData unit)
        {
            var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (alive.Count == 0) return new Position(0f, 0f);
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }
    }
}
