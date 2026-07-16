namespace FDG.Network.Messages
{
    /// <summary>
    /// A client's request to set a player's lobby colour pick (#221): <see cref="ColorIndex"/> is an index
    /// into the front end's palette (opaque to the engine), or -1 to clear the pick. Client to host only,
    /// mirroring <see cref="ArmyListUpdateMessage"/>; the host applies it (last-writer-wins unless another
    /// player already holds the colour) and rebroadcasts the roster.
    /// </summary>
    public record PlayerColorUpdateMessage(PlayerID PlayerID, int ColorIndex);
}
