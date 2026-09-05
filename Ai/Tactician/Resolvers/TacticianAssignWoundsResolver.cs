using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Wound assignment that preserves output (#191 A4-4) instead of the solo bot's list-order
    /// AutoFill. The engine's assignment machinery already enforces every ordering rule (mandatory
    /// pre-assign to wounded models, hero last, finish-a-model-before-starting-fresh), and
    /// TryAddWounds pours a model's full remaining capacity per pick - so the entire decision is
    /// WHICH model to fill next. Greedy rule, per pick among the legal recipients: minimize output
    /// lost per wound absorbed. Killing a plain rifleman costs its whole weapon score for 1 wound;
    /// pouring the pool's tail into a Tough model that survives costs only a discounted fraction -
    /// so mixed units lose their cheap bodies first and multi-wound models soak partial volleys.
    /// Weapon score is a static heuristic (attacks x AP factor); weapon special rules (Deadly,
    /// Blast) are not weighed - recorded gap, revisit if benches show it mattering.
    /// <para>
    /// <b>Objective-aware since 2026-09-05 (#191 step 10 P0, Chris's GUI game):</b> "an enemy unit
    /// partially on an objective took losses, and assigned wounds that killed off models that were
    /// ON the objective, such that the unit was no longer holding it. A human would never." The
    /// output rule alone is marker-blind - with equal weapons it killed in list order, and a heavy
    /// gun off the marker outranked the last body on it. Now a model within the seizure radius of a
    /// marker the unit is holding or contesting carries an extra cost when it would die: a marker
    /// stake (<see cref="TacticianWeights.WoundObjectiveHold"/>, round-scaled like every other
    /// objective term) split across the unit's models still standing on that marker, so the LAST
    /// model on it is worth the whole stake and dies after everything else. When another allied
    /// unit is also on the marker the unit's presence is redundant and the stake is discounted, so
    /// output decides again. Below the search seam: fixes A and B at once. A resolver built
    /// without table state (tests, the bare fallback) is the plain output rule.
    /// </para>
    /// </summary>
    public class TacticianAssignWoundsResolver : IStageResolver<AssignWoundsRequest, AssignWoundsResults>
    {
        // A surviving (chipped) model keeps shooting this round; its cost is only the risk that the
        // mandatory pre-assign rule finishes it next volley. Half its proportional value, per wound.
        private const float SurvivorDiscount = 0.5f;

        // P0: when another allied unit already stands on the marker, this unit's presence there is
        // insurance, not the holding itself - a fifth of the stake (below a heavy gunner: 2 + 1 < 3.45).
        private const float RedundantPresenceFactor = 0.2f;

        private readonly ITableState? _tableState;

        public TacticianAssignWoundsResolver(ITableState? tableState = null)
        {
            _tableState = tableState;
        }

        public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
        {
            var results = new AssignWoundsResults(request.UnitReceivingWounds, request.TotalWoundsToAssign);
            MarkerStakes? stakes = _tableState == null
                ? null
                : MarkerStakes.Of(_tableState, request.UnitReceivingWounds.GetValue(), results);

            while (!results.IsFinishedAssigning)
            {
                PendingWounds? pick = null;
                float bestCost = float.MaxValue;
                foreach (PendingWounds entry in results.PendingWounds)
                {
                    if (!results.CanAssignWoundTo(entry)) continue;
                    float cost = CostPerWound(entry, results, stakes);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        pick = entry;
                    }
                }

                // No legal pick, or the pick is refused (both should be impossible while wounds
                // remain): never fault the stage - AutoFill places the rest exactly like solo (G3).
                if (pick == null || !results.TryAddWounds(pick.Model))
                {
                    results.AutoFill();
                    break;
                }
            }

            return Task.FromResult(results);
        }

        private static float CostPerWound(PendingWounds entry, AssignWoundsResults results, MarkerStakes? stakes)
        {
            ModelData model = entry.Model.GetValue();
            float capacity = model.TotalWounds - model.WoundsDealt - entry.Wounds;
            float poolLeft = results.TotalWoundsToAssign - results.TotalAssignedWounds;
            float absorbed = Math.Min(capacity, poolLeft);
            if (absorbed <= AssignWoundsResults.WoundEpsilon) return float.MaxValue;

            float value = ModelOutputValue(model);
            bool dies = absorbed >= capacity - AssignWoundsResults.WoundEpsilon;
            float loss = dies
                ? value + (stakes?.LossIfDies(entry, results) ?? 0f)
                : value * (absorbed / Math.Max(1f, model.TotalWounds)) * SurvivorDiscount;
            return loss / absorbed;
        }

        // Static per-model output score: total attacks weighted by AP. Enough to rank a heavy
        // gunner above a rifleman and a gun above a fist; not a damage estimate.
        private static float ModelOutputValue(ModelData model)
        {
            float value = 0f;
            foreach (Weapon weapon in model.Weapons)
                value += weapon.Attacks * (1f + 0.15f * weapon.ArmorPenetration);
            return value;
        }

        /// <summary>
        /// P0: the unit's marker presence, priced once per request. One stake per marker the unit
        /// has a living model within the seizure radius of (holding it, or contesting it - under
        /// the reconcile rules one body in range is a full denial, so both count); per pending
        /// model, which of those markers it stands on.
        /// </summary>
        private sealed class MarkerStakes
        {
            private readonly float[] _stakeByMarker;
            private readonly Dictionary<PendingWounds, int> _markerMaskByEntry;

            private MarkerStakes(float[] stakeByMarker, Dictionary<PendingWounds, int> markerMaskByEntry)
            {
                _stakeByMarker = stakeByMarker;
                _markerMaskByEntry = markerMaskByEntry;
            }

            public static MarkerStakes? Of(ITableState tableState, UnitData unit, AssignWoundsResults results)
            {
                if (!TacticalAnalysis.CanSeizeObjectives(unit)) return null;
                List<IObjective> markers = tableState.Objectives.Objects.ToList();
                if (markers.Count == 0) return null;

                // Which pending models stand on which markers (bit i = marker i).
                var masks = new Dictionary<PendingWounds, int>();
                bool anyPresence = false;
                foreach (PendingWounds entry in results.PendingWounds)
                {
                    ModelData model = entry.Model.GetValue();
                    int mask = 0;
                    for (int i = 0; i < markers.Count && i < 31; i++)
                    {
                        float distance = BaseShapeGeometry.SurfaceDistanceToPoint2D(
                            model.BaseShape, model.Position, model.Facing, markers[i].Position);
                        if (distance <= TacticalAnalysis.ObjectiveSeizureRadiusInches) mask |= 1 << i;
                    }
                    masks[entry] = mask;
                    anyPresence |= mask != 0;
                }
                if (!anyPresence) return null;

                int totalRounds = Math.Max(1, tableState.Progress.TotalRounds);
                int round = Math.Clamp(tableState.Progress.RoundCount ?? 1, 1, totalRounds);
                float stake = TacticianWeights.WoundObjectiveHold
                    * TacticianPlanner.ObjectiveUrgency(round, totalRounds);

                var stakes = new float[markers.Count];
                for (int i = 0; i < markers.Count && i < 31; i++)
                {
                    bool anyModelHere = false;
                    foreach (int mask in masks.Values) anyModelHere |= (mask & (1 << i)) != 0;
                    if (!anyModelHere) continue;
                    stakes[i] = stake * (AlliedPresence(tableState, unit, markers[i].Position)
                        ? RedundantPresenceFactor : 1f);
                }
                return new MarkerStakes(stakes, masks);
            }

            /// <summary>
            /// What the unit's marker presence loses if this model dies now: for every marker it
            /// stands on, the stake split evenly across the models still standing there (dying
            /// models already assigned their full capacity no longer count) - so the last one
            /// pays the whole stake.
            /// </summary>
            public float LossIfDies(PendingWounds entry, AssignWoundsResults results)
            {
                int mask = _markerMaskByEntry.GetValueOrDefault(entry);
                if (mask == 0) return 0f;
                float loss = 0f;
                for (int i = 0; i < _stakeByMarker.Length; i++)
                {
                    if ((mask & (1 << i)) == 0 || _stakeByMarker[i] <= 0f) continue;
                    int standing = 0;
                    foreach (PendingWounds other in results.PendingWounds)
                    {
                        if ((_markerMaskByEntry.GetValueOrDefault(other) & (1 << i)) == 0) continue;
                        if (ReferenceEquals(other, entry) || !IsDoomed(other)) standing++;
                    }
                    loss += _stakeByMarker[i] / Math.Max(1, standing);
                }
                return loss;
            }

            private static bool IsDoomed(PendingWounds entry)
            {
                ModelData model = entry.Model.GetValue();
                return model.TotalWounds - model.WoundsDealt - entry.Wounds <= AssignWoundsResults.WoundEpsilon;
            }

            // Another allied unit (team-aware, #296) with a seize-eligible living model on the marker.
            private static bool AlliedPresence(ITableState tableState, UnitData unit, Position marker)
            {
                foreach (IUnit other in tableState.Units.Objects)
                {
                    if (ReferenceEquals(other, unit) || other.ID.Equals(unit.ID)) continue;
                    if (!TacticalAnalysis.AreAllied(tableState, unit.PlayerID, other.PlayerID)) continue;
                    if (!other.GetIsAlive() || !TacticalAnalysis.CanSeizeObjectives(other)) continue;
                    if (TacticalAnalysis.MinBaseEdgeDistanceToPoint(other, marker)
                        <= TacticalAnalysis.ObjectiveSeizureRadiusInches)
                        return true;
                }
                return false;
            }
        }
    }
}
