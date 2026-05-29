using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Movement_OnChargeDeclared"/> alongside
    /// <see cref="EHookID.Movement_OnMoveActionDeclared"/> for a Charge. Carries both
    /// the charging unit and its target so charge-specific rules can react — including
    /// rules attached to the <see cref="Target"/> that penalise the charger's
    /// movement (Melee Shrouding -3").
    /// </summary>
    public sealed record ChargeDeclaredContext(
        IUnit Charger, IUnit Target, float BaseDistanceInches) : IHookContext
    {
        public EHookID Hook => EHookID.Movement_OnChargeDeclared;
    }
}
