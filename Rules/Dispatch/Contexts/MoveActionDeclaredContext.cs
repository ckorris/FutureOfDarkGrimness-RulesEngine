using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Movement_OnMoveActionDeclared"/>: the player has
    /// chosen Advance/Rush/Charge and the distance budget is being computed. Carries
    /// the declared <see cref="ActionType"/> and the unmodified base distance so
    /// movement-bonus rules (Fast, Slow, Rapid Rush) can adjust it.
    /// </summary>
    public sealed record MoveActionDeclaredContext(
        IUnit Unit, EActionType ActionType, float BaseDistanceInches) : IHookContext
    {
        public EHookID Hook => EHookID.Movement_OnMoveActionDeclared;
    }
}
