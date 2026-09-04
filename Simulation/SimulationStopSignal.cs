namespace FDG.Simulation
{
    /// <summary>
    /// Thrown from <see cref="IActivationBoundaryHook"/> to end a simulated game at the end of its
    /// prescribed line (#191 B1 step 5c). This is B0's proven throw-stop: the exception unwinds the
    /// state machine, and <see cref="GameModel.FDGServer"/> completes the game rather than leaving
    /// it running. Measured 30/30 with zero heap growth over 400 simulations at 4k; the alternative
    /// (orphan the tasks and walk away) leaks and keeps burning CPU, so it is not used.
    /// <para>
    /// Its own type, rather than a generic exception, so FDGServer can end the game QUIETLY: a
    /// search runs thousands of these, and formatting a state-machine stack trace to the console for
    /// each one is both noise and real cost. An engine fault still prints in full.
    /// </para>
    /// </summary>
    public sealed class SimulationStopSignal : Exception
    {
        public SimulationStopSignal() : base("simulation: end of prescribed line") { }
    }
}
