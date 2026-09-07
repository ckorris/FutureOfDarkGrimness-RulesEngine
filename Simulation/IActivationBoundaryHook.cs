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
    /// Stopping is cooperative: an implementation that wants the simulated game to end here returns
    /// <c>true</c>, and <see cref="Stages.DeterminePlayerTurnStage"/> completes the game the way
    /// <see cref="Stages.VictoryCalculationStage"/> does at a natural end - it notifies completion and
    /// returns without activating a transition, so every frame of the state machine's transition
    /// chain completes normally. This replaced B0's throw-stop (<see cref="SimulationStopSignal"/>)
    /// for #191 R9: the chain is a nested await per transition, so a thrown stop was re-thrown at
    /// every one of its ~70-190 frames, and under an attached debugger each re-throw is a
    /// stop-the-process event - the GUI freeze at the Strategist's first activation. ABANDON
    /// (orphaning the tasks) is still deliberately not used: the game must actually end.
    /// </para>
    /// </summary>
    public interface IActivationBoundaryHook
    {
        /// <param name="actingPlayer">
        /// The player whose activation is about to be resolved - already determined (including the
        /// #197 P19 activates-next override), so a prescription can be set on the right policy.
        /// </param>
        /// <returns><c>true</c> to end the simulated game at this boundary; <c>false</c> to play on.</returns>
        Task<bool> AtActivationBoundary(PlayerID actingPlayer);
    }
}
