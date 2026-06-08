using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Movement_OnMoveThroughEnemy"/>: the moving unit's path passed through
    /// (not merely near) an enemy unit's footprint. The trigger point for mid-move attacks (Strafing).
    /// <see cref="Unit"/> is the mover — the acting unit the offer addresses; the specific enemy moved
    /// through is supplied as the ability's target at resolution time, so it isn't carried here.
    /// </summary>
    public sealed record MoveThroughEnemyContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Movement_OnMoveThroughEnemy;

        public IUnit ActingUnit => Unit;
    }
}
