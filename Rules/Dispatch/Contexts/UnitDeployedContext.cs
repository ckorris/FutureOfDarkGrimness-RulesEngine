using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Deployment_OnUnitDeployed"/>: a unit's deployment placement
    /// has just been committed. The trigger point at which post-deploy activated abilities
    /// (Vanguard's reposition; the Scout/Ambush family later) are offered.
    /// </summary>
    public sealed record UnitDeployedContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Deployment_OnUnitDeployed;

        public IUnit ActingUnit => Unit;
    }
}
