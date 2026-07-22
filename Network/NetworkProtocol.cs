using FDG.Data;

namespace FDG.Network
{
    /// <summary>
    /// The compatibility contract a client and host must agree on before a client may join a lobby (#075).
    /// Two dimensions are checked:
    /// <list type="bullet">
    /// <item><b>Protocol version</b> — a manual counter bumped whenever the wire format changes in a way the
    /// type-map hash can't catch (message-registry changes, enum encoding, framing). Both ends compare their
    /// compiled-in <see cref="Version"/>.</item>
    /// <item><b>Type-map fingerprint</b> — an automatic hash of the registered store type map
    /// (<see cref="GameDataStore.GetTypeMapFingerprint"/>). Catches store-shape drift, which would otherwise
    /// silently corrupt replicated data because the positional TypeID is baked into every serialized
    /// reference.</item>
    /// </list>
    /// The host validates a connecting client's reported pair against its own and refuses the join with a
    /// readable reason on any mismatch, before the client is added to the roster.
    /// </summary>
    public static class NetworkProtocol
    {
        /// <summary>
        /// The wire-protocol version this build speaks. Bump when the wire format changes in a way the
        /// type-map fingerprint cannot detect (e.g. enum encoding, message registry, framing).
        /// v2: added the lobby password to NewLobbyClientGreeting (QF1) and single-buffer frame writes (QF4).
        /// v3: added PlayerColorUpdateMessage + LobbyPlayerInfoSummary.ColorIndex (#221 lobby colour sync).
        /// v4: added PlayerTeamUpdateMessage (#255 lobby team selection).
        /// </summary>
        public const int Version = 4;

        private static string? _localTypeMapHash;

        /// <summary>
        /// This build's type-map fingerprint, computed once from a default store. Both host and client build
        /// their default store the same way, so a build mismatch shows up as a hash mismatch.
        /// </summary>
        public static string LocalTypeMapHash =>
            _localTypeMapHash ??= GameDataStore.GameDataStoreBuilder.GetDefault().GetTypeMapFingerprint();

        /// <summary>
        /// Validates a connecting client's reported protocol version and type-map fingerprint against this
        /// (host) build. Returns <c>true</c> when compatible; otherwise returns <c>false</c> and sets
        /// <paramref name="rejectReason"/> to a player-readable explanation.
        /// </summary>
        public static bool TryValidateJoin(int clientVersion, string? clientTypeMapHash, out string rejectReason)
        {
            if (clientVersion != Version)
            {
                rejectReason = $"Incompatible game version: the server speaks protocol v{Version} but your " +
                    $"client sent v{clientVersion}. Both must run the same build.";
                return false;
            }

            if (clientTypeMapHash != LocalTypeMapHash)
            {
                rejectReason = "Incompatible game data: your client's data layout doesn't match the server's. " +
                    "Both must run the same build.";
                return false;
            }

            rejectReason = string.Empty;
            return true;
        }
    }
}
