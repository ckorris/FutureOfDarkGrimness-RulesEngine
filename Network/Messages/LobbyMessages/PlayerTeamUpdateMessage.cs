using FDG.Players;

namespace FDG.Network.Messages
{
    /// <summary>
    /// A client's request to set a player's lobby team (#255). Client to host only, mirroring
    /// <see cref="PlayerColorUpdateMessage"/>; the host applies it (rejecting teams outside
    /// 1..playerCount) and rebroadcasts the roster. Unlike colours, teams are shared by design -
    /// no uniqueness check.
    /// </summary>
    public record PlayerTeamUpdateMessage(PlayerID PlayerID, ETeamOption TeamNumber);
}
