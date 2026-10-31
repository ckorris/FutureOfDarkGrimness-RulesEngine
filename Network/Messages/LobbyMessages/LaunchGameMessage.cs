
namespace FDG.Network.Messages
{
    /// <summary>
    /// Broadcast host -> clients when the game launches.
    /// </summary>
    internal class LaunchGameMessage
    {
        /// <summary>
        /// #412: a random ID for this game, minted by the host at launch, so the front end's anonymous
        /// usage events (the host's game_start/game_end, each client's game_join) can be paired up
        /// without identifying anyone. Optional on the wire: an older host sends none (null here), and
        /// an older client ignores it (Newtonsoft skips unknown members), so no protocol bump.
        /// </summary>
        public string? GameId { get; set; }

        /// <summary>
        /// #451: the host's replay header, so each client's own recording knows the game's persistent ID, seed,
        /// settings and slots from its first event (in a new game the synced store only gets them at the first
        /// activation). Null when the host is not recording - its recording failed to start.
        /// </summary>
        public Replay.ReplayHeader? Replay { get; set; }

        /// <summary>
        /// #453: in a resumed game, the host's recording through its <c>Resumed</c> marker (gzip <c>.fdgreplay</c>
        /// bytes, base64 on the wire) - the saved game's history, so each client's recording continues it rather than
        /// starting at the join. Here rather than in a message of its own so it arrives before anything the client
        /// records. Null for a new game, a resume without a replay, or a history too large to send.
        /// </summary>
        public byte[]? ReplayHistory { get; set; }
    }
}
