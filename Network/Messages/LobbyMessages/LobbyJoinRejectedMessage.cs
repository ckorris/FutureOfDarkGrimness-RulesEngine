namespace FDG.Network.Messages
{
    /// <summary>
    /// Sent by the host to a single connecting client when its protocol version or store type-map
    /// fingerprint doesn't match the host's (#075). The client surfaces <see cref="Reason"/> to the player
    /// and abandons the join. A primitive string payload, so it deserializes regardless of any wire-format
    /// drift between the two builds.
    /// </summary>
    public record LobbyJoinRejectedMessage(string Reason);
}
