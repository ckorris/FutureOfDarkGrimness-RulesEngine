namespace FDG.Simulation
{
    /// <summary>
    /// B0's throw-stop (#191 B1 step 5c): thrown from a boundary hook to end a simulated game, unwinding
    /// the state machine into <see cref="GameModel.FDGServer"/>'s catch. <b>No longer thrown by the
    /// engine's own hook</b> (#191 R9): the transition chain is a nested await per transition, so the
    /// signal was re-thrown at every one of its frames, and under an attached debugger each re-throw is
    /// a stop-the-process event. <see cref="IActivationBoundaryHook"/> now stops cooperatively by
    /// returning true. The type and FDGServer's catch stay so a hook that still throws it ends its game
    /// quietly rather than as a fault.
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
