using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// A per-activation cache of one planned, engine-valid move (#191). Extracted from
    /// TacticianPlanner so TacticianMovementResolver can play out any planner's cached move -
    /// the Gunline profile's scripted planner shares the executor instead of duplicating its
    /// budget/path re-validation.
    /// </summary>
    public interface IMovePlanSource
    {
        /// <summary>
        /// The cached move for this unit's DefineMovementPathRequest, or null (fall back to solo).
        /// Cleared on hand-out - each activation moves at most once.
        /// </summary>
        List<ModelMoveEntry>? TakePlannedMove(DataBinding<UnitData> unit);
    }
}
