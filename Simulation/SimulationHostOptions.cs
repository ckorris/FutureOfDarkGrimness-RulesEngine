using FDG.Players;

namespace FDG.Simulation
{
    /// <summary>
    /// What <see cref="GameModel.FDGServer"/> needs to host a SIMULATED game rather than a real one
    /// (#191 B1 step 5c). Passed only by <see cref="SimulationService"/>; null everywhere else, so
    /// every real game keeps the message bus, the JSON request path and no boundary hook.
    /// </summary>
    public sealed class SimulationHostOptions
    {
        /// <summary>The pause/step point at each activation boundary. See <see cref="IActivationBoundaryHook"/>.</summary>
        public IActivationBoundaryHook? BoundaryHook { get; init; }

        /// <summary>
        /// Replaces the bus-and-JSON request path with a direct call into each slot's resolver
        /// registry. See <see cref="DirectPlayerRequester"/>.
        /// </summary>
        public IPlayerRequestByID? PlayerRequester { get; init; }
    }
}
