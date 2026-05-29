using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Deployment_OnPreDeploymentSelect"/>: a unit is
    /// about to be placed during normal deployment. Ambush and Scout pull themselves
    /// out of the pool here for later placement by their own stages.
    /// </summary>
    public sealed record PreDeploymentSelectContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Deployment_OnPreDeploymentSelect;
    }
}
