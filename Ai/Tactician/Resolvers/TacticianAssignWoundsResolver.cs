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
    /// </summary>
    public class TacticianAssignWoundsResolver : IStageResolver<AssignWoundsRequest, AssignWoundsResults>
    {
        // A surviving (chipped) model keeps shooting this round; its cost is only the risk that the
        // mandatory pre-assign rule finishes it next volley. Half its proportional value, per wound.
        private const float SurvivorDiscount = 0.5f;

        public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
        {
            var results = new AssignWoundsResults(request.UnitReceivingWounds, request.TotalWoundsToAssign);

            while (!results.IsFinishedAssigning)
            {
                PendingWounds? pick = null;
                float bestCost = float.MaxValue;
                foreach (PendingWounds entry in results.PendingWounds)
                {
                    if (!results.CanAssignWoundTo(entry)) continue;
                    float cost = CostPerWound(entry, results);
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

        private static float CostPerWound(PendingWounds entry, AssignWoundsResults results)
        {
            ModelData model = entry.Model.GetValue();
            float capacity = model.TotalWounds - model.WoundsDealt - entry.Wounds;
            float poolLeft = results.TotalWoundsToAssign - results.TotalAssignedWounds;
            float absorbed = Math.Min(capacity, poolLeft);
            if (absorbed <= AssignWoundsResults.WoundEpsilon) return float.MaxValue;

            float value = ModelOutputValue(model);
            bool dies = absorbed >= capacity - AssignWoundsResults.WoundEpsilon;
            float loss = dies
                ? value
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
    }
}
