using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Ai.Tactician;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Gunline
{
    /// <summary>
    /// Scripted decision state for the Gunline profile (#191 tooling): a stand-in for a human
    /// playing a defensive shooting army, so the benchmark stops being blind to "melee horde vs a
    /// line that HOLDS" (the solo bot advances, so that matchup never occurs in automated play).
    /// The script, in priority order: stand and shoot whatever is in range; otherwise claim an
    /// objective no enemy is near; otherwise hold the line. It never charges and never walks
    /// toward the enemy. Deliberately predictable - this is measurement apparatus, not an
    /// opponent meant to win.
    /// </summary>
    public sealed class GunlinePlanner : IMovePlanSource
    {
        /// <summary>An objective is "safe" while no living enemy is within this of its marker.</summary>
        public const float SafeObjectiveEnemyRadiusInches = 18f;

        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;
        private readonly Action<string>? _decisionLog;

        private DataBinding<UnitData>? _activeUnit;
        private MacroAction? _plan;

        public GunlinePlanner(ITableState tableState, RuleEvaluator evaluator,
            Action<string>? decisionLog = null)
        {
            _tableState = tableState;
            _evaluator = evaluator;
            _decisionLog = decisionLog;
        }

        /// <summary>Called by the activation resolver the moment it picks the unit.</summary>
        public void BeginActivation(DataBinding<UnitData> unit)
        {
            _activeUnit = unit;
            _plan = null;
        }

        /// <summary>
        /// The script's Choose Action answer, or null when it has no claim (no known active unit,
        /// choice not among the valid options) - the caller falls back to solo behavior (G3).
        /// </summary>
        public string? ChooseAction(IReadOnlyList<string> validOptions)
        {
            if (_activeUnit == null) return null;

            // Post-move re-entry: the claim move was played out; shoot if still allowed, else end.
            if (_plan != null)
            {
                if (validOptions.Contains(ChooseActionStage.SHOOT_CHOICE_NAME))
                    return ChooseActionStage.SHOOT_CHOICE_NAME;
                return validOptions.Contains(ChooseActionStage.PASS_CHOICE_NAME)
                    ? ChooseActionStage.PASS_CHOICE_NAME : null;
            }

            // 1. Stand and shoot. The engine only offers Shoot when a legal target exists, so the
            // range check just keeps us from burning the activation lifting a rifle at nothing.
            if (validOptions.Contains(ChooseActionStage.SHOOT_CHOICE_NAME) && AnyEnemyInRange())
            {
                _decisionLog?.Invoke($"gunline {UnitName()} -> Shoot (enemy in range, holding)");
                return ChooseActionStage.SHOOT_CHOICE_NAME;
            }

            // 2. Claim a safe objective (nothing to shoot from here).
            if (validOptions.Contains(ChooseActionStage.MOVEMENT_CHOICE_NAME))
            {
                MacroAction? claim = BestSafeObjectiveMove();
                if (claim != null)
                {
                    _plan = claim;
                    _decisionLog?.Invoke($"gunline {UnitName()} -> Move [{claim.Intent}] " +
                        $"end=({claim.ProjectedCentroid.x:F1},{claim.ProjectedCentroid.z:F1}) " +
                        $"obj=({claim.TargetObjective?.Position.x:F0},{claim.TargetObjective?.Position.z:F0})");
                    return ChooseActionStage.MOVEMENT_CHOICE_NAME;
                }
            }

            // 3. Hold the line.
            if (validOptions.Contains(ChooseActionStage.PASS_CHOICE_NAME))
            {
                _decisionLog?.Invoke($"gunline {UnitName()} -> Pass (nothing in range, no safe objective)");
                return ChooseActionStage.PASS_CHOICE_NAME;
            }
            return null;
        }

        public List<ModelMoveEntry>? TakePlannedMove(DataBinding<UnitData> unit)
        {
            if (_activeUnit == null || _plan == null) return null;
            if (!ReferenceEquals(unit, _activeUnit) && !unit.Reference.Equals(_activeUnit.Reference)) return null;
            List<ModelMoveEntry> move = _plan.Move;
            return move.Count == 0 ? null : move;
        }

        private bool AnyEnemyInRange()
        {
            UnitData self = _activeUnit!.GetValue();
            foreach (DataBinding<UnitData> enemy in EnemyBindings(self.PlayerID))
            {
                float distance = Utilities.UnitCompareUtilities.MinDistanceBetweenUnits(
                    self, enemy.GetValue(), out _, out _, includeVertical: true);
                if (distance <= TacticalAnalysis.MaxWeaponRange(self, enemy.GetValue(), _evaluator))
                    return true;
            }
            return false;
        }

        // The macro generator's best objective candidate whose marker is safe (no enemy within
        // SafeObjectiveEnemyRadiusInches) and not already projected ours: rush candidates first
        // (nothing to shoot, so cover ground), nearest-to-goal endpoint wins.
        private MacroAction? BestSafeObjectiveMove()
        {
            UnitData self = _activeUnit!.GetValue();
            var safe = new HashSet<IObjective>();
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                if (projection.ProjectedOwner.HasValue && projection.ProjectedOwner.Value == self.PlayerID)
                    continue; // already ours; sticky until contested, so no babysitting
                bool enemyNear = EnemyBindings(self.PlayerID).Any(enemy =>
                    TacticalAnalysis.MinBaseEdgeDistanceToPoint(
                        enemy.GetValue(), projection.Objective.Position) <= SafeObjectiveEnemyRadiusInches);
                if (!enemyNear) safe.Add(projection.Objective);
            }
            if (safe.Count == 0) return null;

            MacroAction? best = null;
            float bestGap = float.MaxValue;
            foreach (MacroAction candidate in MacroActionGenerator.Enumerate(_evaluator, _tableState, _activeUnit!))
            {
                if (candidate.Intent is not (EMacroIntent.AdvanceOnObjective or EMacroIntent.RushObjective)) continue;
                if (candidate.Feasibility == EFeasibility.Blocked) continue;
                if (candidate.TargetObjective == null || !safe.Contains(candidate.TargetObjective)) continue;
                if (candidate.Move.Count == 0) continue;
                float gap = Position.GetDistance2D(candidate.ProjectedCentroid, candidate.TargetObjective.Position);
                // Rush covers more ground and nothing was in range to shoot anyway.
                if (candidate.Intent == EMacroIntent.AdvanceOnObjective) gap += 1f;
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = candidate;
                }
            }
            return best;
        }

        private string UnitName() => _activeUnit?.GetValue().Name ?? "?";

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
    }
}
