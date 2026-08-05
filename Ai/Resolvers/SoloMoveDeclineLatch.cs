namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #358: the one-way handshake between the solo bot's path resolver and its action menu.
    /// <para>
    /// When <see cref="AiDefineMovementResolver"/> declines the MAIN activation move (no legal
    /// path exists for a wedged unit), the engine's Back affordance returns to Choose Action -
    /// where the deterministic Charge > Move > Shoot > Pass policy would pick Move again, decline
    /// again, and loop the menu forever (~1.5M decisions until the bench watchdog fired). The
    /// resolver arms this latch on such a decline; the next
    /// <see cref="AiStringSelectionResolver"/> action pick consumes it and skips the movement
    /// family once, ending the activation with Shoot/Pass instead. One instance is shared by the
    /// two resolvers of a solo set (the engine runs a player's stages sequentially, so the arming
    /// decline and the next menu belong to the same activation by construction). Declining an
    /// optional rule-triggered move never arms it - that decline is final, not menu-reopening.
    /// </para>
    /// </summary>
    public sealed class SoloMoveDeclineLatch
    {
        private bool _armed;

        public void Arm() => _armed = true;

        /// <summary>True exactly once per <see cref="Arm"/> - reading clears it.</summary>
        public bool Consume()
        {
            bool wasArmed = _armed;
            _armed = false;
            return wasArmed;
        }
    }
}
