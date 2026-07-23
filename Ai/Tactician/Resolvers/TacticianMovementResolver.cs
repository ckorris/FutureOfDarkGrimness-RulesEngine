using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Plays out the planner's cached macro-action when the engine asks for the actual move
    /// (#191 A4-2). The cached move was built by the MovementPlanner ladder, so it is engine-valid
    /// by construction - but it is re-checked against THIS request's budgets before submitting,
    /// because a rejection would fault the stage with no retry; anything without a valid plan
    /// falls back to the solo-rules movement resolver (G3).
    /// </summary>
    public class TacticianMovementResolver
        : IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>
    {
        private readonly IMovePlanSource _planner;
        private readonly ITableState _tableState;
        private readonly IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>> _soloFallback;

        public TacticianMovementResolver(IMovePlanSource planner, ITableState tableState,
            IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>> soloFallback)
        {
            _planner = planner;
            _tableState = tableState;
            _soloFallback = soloFallback;
        }

        public Task<CancellableResult<List<ModelMoveEntry>>> Resolve(DefineMovementPathRequest request)
        {
            List<ModelMoveEntry>? planned = _planner.TakePlannedMove(request.UnitDataBinding);
            if (planned == null)
                return _soloFallback.Resolve(request);

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };
            var enemies = MovementPlanner.LiveEnemyFootprints(_tableState,
                request.UnitDataBinding.GetValue().PlayerID);
            // #205: reject a planned macro-move that would end stacked on a friendly, falling back to the solo
            // resolver (which re-plans around friendlies) rather than submitting a move the stage rejects.
            var friendlies = MovementPlanner.LiveFriendlyFootprints(_tableState,
                request.UnitDataBinding.GetValue().PlayerID, request.UnitDataBinding.GetValue().ID);
            // #159: lenientCoherency mirrors DefinePathStage so this pre-check agrees with the authoritative
            // one - a plan that holds an already-broken unit isn't rejected for a coherency it can't restore.
            bool valid = MovementUtilities.ValidatePaths(planned, budgetFor, enemies,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects.ToList(), out _, friendlies,
                lenientCoherency: true);

            if (valid)
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(
                    new Selected<List<ModelMoveEntry>>(planned));

            // #264 issue 7 (with #216's repair pass): the plan was built at Choose Action time, before
            // this request existed, so its budgets can disagree with the request's per-model ones - a
            // joined Slow hero makes EVERY planned move fail here, and the unit then plays the solo
            // resolver for the whole game without anything saying so. The macro choice was still the
            // right one; only its distance was wrong. Re-run the same ladder toward the same
            // destination under THIS request's budgets before conceding the activation.
            List<ModelMoveEntry>? repaired = TryRepairWithinRequestBudgets(request, planned, budgetFor,
                enemies, friendlies);
            if (repaired != null)
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(
                    new Selected<List<ModelMoveEntry>>(repaired));

            return _soloFallback.Resolve(request);
        }

        // Re-plan toward the cached move's own destination at the SLOWEST model's allowance, with each
        // model validated against its own cap. Null when even that cannot be made legal (the solo
        // resolver, which re-plans from scratch, is the right answer then).
        private List<ModelMoveEntry>? TryRepairWithinRequestBudgets(DefineMovementPathRequest request,
            List<ModelMoveEntry> planned, Func<ModelMoveEntry, ModelMoveBudget> budgetFor,
            List<EnemyModelFootprint> enemies, List<EnemyModelFootprint> friendlies)
        {
            DataBinding<UnitData> unit = request.UnitDataBinding;
            var living = unit.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return null;

            var ends = planned.Where(e => e.Positions.Count > 0)
                .Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0) return null;
            var goal = new Position(ends.Average(p => p.x), ends.Average(p => p.z));

            float rush = float.MaxValue, hardCap = float.MaxValue;
            foreach (DataBinding<ModelData> model in living)
            {
                var (_, modelRush, modelMax) = request.BudgetFor(model.GetValue().ID);
                rush = Math.Min(rush, modelRush);
                hardCap = Math.Min(hardCap, modelMax);
            }
            if (hardCap <= 0f) return null;

            List<ModelMoveEntry> repaired = MovementPlanner.PlanMoveToward(unit, living, _tableState,
                goal, Math.Max(0f, Math.Min(rush, hardCap) - 0.001f), hardCap, budgetFor,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain);

            bool valid = MovementUtilities.ValidatePaths(repaired, budgetFor, enemies,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects.ToList(), out _,
                friendlies, lenientCoherency: true);
            return valid ? repaired : null;
        }
    }
}
