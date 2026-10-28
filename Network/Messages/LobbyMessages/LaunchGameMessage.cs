
namespace FDG.Network.Messages
{
    /// <summary>
    /// Broadcast host -> clients when the game launches.
    /// </summary>
    internal class LaunchGameMessage
    {
        /// <summary>
        /// #406: a random ID for this game, minted by the host at launch, so the front end's anonymous
        /// usage events (the host's game_start/game_end, each client's game_join) can be paired up
        /// without identifying anyone. Optional on the wire: an older host sends none (null here), and
        /// an older client ignores it (Newtonsoft skips unknown members), so no protocol bump.
        /// </summary>
        public string? GameId { get; set; }
    }
}
