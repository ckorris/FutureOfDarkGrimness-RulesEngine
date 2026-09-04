using FDG.Players;

namespace FDG.Simulation
{
    /// <summary>
    /// The engine's single simulation seam (#191 B1 step 5c; campaign decision D10a, pre-authorized):
    /// a pause/step point at the activation boundary, called from
    /// <see cref="Stages.DeterminePlayerTurnStage"/> once the next activating player is known and
    /// before any decision for that activation is requested.
    /// <para>
    /// <b>Null in all real play.</b> <see cref="IGameContext.ActivationBoundaryHook"/> defaults to
    /// null and is set only by <see cref="SimulationService"/>, so a normal game (GUI, headless,
    /// networked, benchmark) never sees this call and cannot be perturbed by it.
    /// </para>
    /// <para>
    /// Why the hook exists: B0 measured a node expansion at 2k as 223ms, of which 54ms is the
    /// load+save of cloning a game per node. A LINE of consecutive activations does not need a clone
    /// per activation - it needs one game instance that stops at each boundary long enough for the
    /// search to hand in the next prescription. That is this call. The search snapshots only where
    /// its tree actually branches.
    /// </para>
    /// <para>
    /// Stopping is by exception: an implementation that wants the simulated game to end here throws
    /// (see <see cref="SimulationService.SimulationStopSignal"/>), which unwinds the state machine
    /// through <see cref="GameModel.FDGServer"/>'s own catch into a completed game. That is the
    /// throw-stop B0 measured at 30/30 with zero heap growth over 400 simulations at 4k; ABANDON
    /// (orphaning the tasks) is deliberately not used.
    /// </para>
    /// </summary>
    public interface IActivationBoundaryHook
    {
        /// <param name="actingPlayer">
        /// The player whose activation is about to be resolved - already determined (including the
        /// #197 P19 activates-next override), so a prescription can be set on the right policy.
        /// </param>
        Task AtActivationBoundary(PlayerID actingPlayer);
    }
}
