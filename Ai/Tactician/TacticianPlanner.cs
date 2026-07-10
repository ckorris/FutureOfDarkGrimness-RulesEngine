using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Per-activation decision state shared by the Tactician's resolvers (#191 A4-2). The engine
    /// asks for the ACTION first (Choose Action) and the movement AFTER (DefineMovementPathRequest),
    /// but a good decision is the (action x macro-action) PAIR - so the action resolver plans once,
    /// caches the winning candidate here, and the movement resolver plays it out. One planner per
    /// controller; the engine thread runs a player's requests sequentially, so no locking.
    /// </summary>
    public sealed class TacticianPlanner
    {
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;

        private DataBinding<UnitData>? _activeUnit;
        private MacroAction? _plan;

        public TacticianPlanner(ITableState tableState, RuleEvaluator evaluator)
        {
            _tableState = tableState;
            _evaluator = evaluator;
        }

        /// <summary>Called by the activation resolver the moment it picks the unit.</summary>
        public void BeginActivation(DataBinding<UnitData> unit)
        {
            _activeUnit = unit;
            _plan = null;
        }

        /// <summary>
        /// Plans this activation and answers Choose Action. Returns null when the planner has no
        /// claim (no known active unit, or its planned choice is not among the valid options) -
        /// the caller then falls back to solo behavior, never faulting the stage (G3).
        /// </summary>
        public string? ChooseAction(IReadOnlyList<string> validOptions)
        {
            if (_activeUnit == null) return null;

            // Post-move re-entry: the plan was played out; shoot if the engine still lets us,
            // otherwise end the activation.
            if (_plan != null)
            {
                if (validOptions.Contains(ChooseActionStage.SHOOT_CHOICE_NAME))
                    return ChooseActionStage.SHOOT_CHOICE_NAME;
                if (validOptions.Contains(ChooseActionStage.PASS_CHOICE_NAME))
                    return ChooseActionStage.PASS_CHOICE_NAME;
                return null;
            }

            List<MacroAction> candidates = MacroActionGenerator.Enumerate(_evaluator, _tableState, _activeUnit);
            MacroAction? best = null;
            string? bestAction = null;
            float bestScore = float.NegativeInfinity;

            foreach (MacroAction candidate in candidates)
            {
                string? action = ActionNameFor(candidate, validOptions);
                if (action == null) continue; // not executable under the currently valid actions

                float score = Score(candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestAction = action;
                }
            }

            if (best == null || bestAction == null) return null;

            // Hold-and-shoot / pass need no movement; movement plans are cached for the move request.
            if (bestAction == ChooseActionStage.MOVEMENT_CHOICE_NAME
                || bestAction == ChooseActionStage.CHARGE_CHOICE_NAME)
            {
                _plan = best;
            }
            else
            {
                _plan = best; // mark the activation as decided so re-entry shoots/passes
            }

            return bestAction;
        }

        /// <summary>
        /// The cached move for this unit's DefineMovementPathRequest, or null (fall back to solo).
        /// Cleared on hand-out - each activation moves at most once.
        /// </summary>
        public List<ModelMoveEntry>? TakePlannedMove(DataBinding<UnitData> unit)
        {
            if (_activeUnit == null || _plan == null) return null;
            if (!ReferenceEquals(unit, _activeUnit) && !unit.Reference.Equals(_activeUnit.Reference)) return null;
            if (_plan.Intent == EMacroIntent.Hold) return null;

            List<ModelMoveEntry> move = _plan.Move;
            return move.Count == 0 ? null : move;
        }

        // --- scoring --------------------------------------------------------------------------------

        /// <summary>
        /// The A4-2 greedy score (plan sec. 8): value-weighted damage dealt from the candidate's
        /// endpoint, minus expected retaliation onto that endpoint, plus the objective-control delta.
        /// Weights live in TacticianWeights; tuning is benchmark-driven and recorded.
        /// </summary>
        public float Score(MacroAction candidate)
        {
            UnitData self = _activeUnit!.GetValue();
            Position end = candidate.ProjectedCentroid;

            float offense = 0f;
            float retaliation = 0f;
            foreach (DataBinding<UnitData> enemyBinding in EnemyBindings(self.PlayerID))
            {
                UnitData enemy = enemyBinding.GetValue();
                Position enemyPos = Centroid(enemy);
                float endDistance = Distance(end, enemyPos);

                if (candidate.Intent == EMacroIntent.ChargeToContact
                    && candidate.TargetEnemy != null && ReferenceEquals(candidate.TargetEnemy, enemy)
                    && candidate.Feasibility == EFeasibility.Reachable)
                {
                    MeleeEstimate melee = CombatMath.EstimateMelee(_evaluator, _activeUnit, enemyBinding);
                    offense = Math.Max(offense,
                        ValueFraction(melee.AttackerAttack.ExpectedWounds, enemy)
                        - ValueFraction(melee.DefenderReturn.ExpectedWounds, self));
                }
                else if (CanShootAfter(candidate))
                {
                    AttackEstimate shot = CombatMath.EstimateShooting(_evaluator, _activeUnit, enemyBinding,
                        new AttackContext(Math.Max(1f, endDistance), AttackerMoved: candidate.Intent != EMacroIntent.Hold));
                    offense = Math.Max(offense, ValueFraction(shot.ExpectedWounds, enemy));
                }

                // What the enemy could do to us at the endpoint next activation (their advance included).
                float theirReach = Math.Max(1f, endDistance - TacticalAnalysis.AdvanceDistance(enemy, _evaluator));
                AttackEstimate incoming = CombatMath.EstimateShooting(_evaluator, enemyBinding, _activeUnit,
                    new AttackContext(theirReach, AttackerMoved: true));
                float incomingValue = ValueFraction(incoming.ExpectedWounds, self);
                // Melee threat: if they can charge the endpoint, count their melee margin too.
                if (TacticalAnalysis.ChargeDistanceAgainst(enemy, self, _evaluator) >= endDistance - 1f
                    && enemy.GetMeleeWeapons().Count > 0)
                {
                    incomingValue = Math.Max(incomingValue, 0.5f * ValueFraction(
                        CombatMath.EstimateMelee(_evaluator, enemyBinding, _activeUnit).AttackerAttack.ExpectedWounds, self));
                }
                retaliation = Math.Max(retaliation, incomingValue);
            }

            float objectiveDelta = ObjectiveDelta(self, end);

            return TacticianWeights.MoveDamage * offense
                 - TacticianWeights.MoveRetaliation * retaliation
                 + TacticianWeights.MoveObjective * objectiveDelta
                 + (candidate.Feasibility == EFeasibility.Reachable ? TacticianWeights.MoveReachableBonus : 0f);
        }

        // +1 for each objective the endpoint newly holds/contests for us, -1 for a held objective the
        // move walks away from with nobody left on it.
        private float ObjectiveDelta(UnitData self, Position end)
        {
            float delta = 0f;
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                bool projectedOurs = projection.ProjectedOwner.HasValue
                    && projection.ProjectedOwner.Value == self.PlayerID;
                float endDist = Distance(end, projection.Objective.Position);
                float nowDist = TacticalAnalysis.MinBaseEdgeDistanceToPoint(self, projection.Objective.Position);
                bool weAreOnItNow = nowDist <= TacticalAnalysis.ObjectiveSeizureRadiusInches;
                // The centroid is a coarse stand-in for base-edge reach; half the seize radius of slack.
                bool endOnIt = endDist <= TacticalAnalysis.ObjectiveSeizureRadiusInches + 1.5f;

                if (!projectedOurs && endOnIt) delta += 1f;
                if (projectedOurs && weAreOnItNow && !endOnIt
                    && projection.PlayersInRange.Count(p => p == self.PlayerID) <= 1)
                    delta -= 1f;
            }
            return delta;
        }

        private static bool CanShootAfter(MacroAction candidate) => candidate.Intent switch
        {
            EMacroIntent.Hold => true,
            EMacroIntent.AdvanceOnObjective or EMacroIntent.EngageAtRange
                or EMacroIntent.SeekCoverFrom or EMacroIntent.MoveToCast or EMacroIntent.Escort => true,
            // Rush-budget intents give up shooting; charge damage is scored as melee above.
            _ => false,
        };

        private static float ValueFraction(float expectedWounds, UnitData target)
        {
            float remaining = Math.Max(1f, target.RemainingWounds);
            return Math.Min(1f, expectedWounds / remaining) * TacticalAnalysis.UnitValue(target) / 100f;
        }

        // Charge maps to Charge; Hold maps to Shoot (stand and fire) or Pass; everything else moves.
        private static string? ActionNameFor(MacroAction candidate, IReadOnlyList<string> validOptions)
        {
            switch (candidate.Intent)
            {
                case EMacroIntent.ChargeToContact:
                    return candidate.Feasibility == EFeasibility.Reachable
                        && validOptions.Contains(ChooseActionStage.CHARGE_CHOICE_NAME)
                        ? ChooseActionStage.CHARGE_CHOICE_NAME : null;
                case EMacroIntent.Hold:
                    if (validOptions.Contains(ChooseActionStage.SHOOT_CHOICE_NAME))
                        return ChooseActionStage.SHOOT_CHOICE_NAME;
                    return validOptions.Contains(ChooseActionStage.PASS_CHOICE_NAME)
                        ? ChooseActionStage.PASS_CHOICE_NAME : null;
                default:
                    return validOptions.Contains(ChooseActionStage.MOVEMENT_CHOICE_NAME)
                        ? ChooseActionStage.MOVEMENT_CHOICE_NAME : null;
            }
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

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
