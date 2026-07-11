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
            bool valid = MovementUtilities.ValidatePaths(planned, budgetFor, enemies,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects.ToList(), out _, friendlies);

            return valid
                ? Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(new Selected<List<ModelMoveEntry>>(planned))
                : _soloFallback.Resolve(request);
        }
    }
}
