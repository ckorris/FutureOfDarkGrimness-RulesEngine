using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Messages
{
    /// <summary>
    /// The first message a client sends on joining. Carries the client's protocol version and store
    /// type-map fingerprint so the host can refuse an incompatible build before adding it to the roster
    /// (#075). These two fields are primitives on purpose, so the greeting deserializes even when the rest
    /// of the wire format differs between builds. An older client that predates the handshake sends neither
    /// field; they default to <c>0</c> / <c>null</c>, which the host treats as a mismatch and rejects.
    /// </summary>
    public record NewLobbyClientGreeting(string PlayerName, int ProtocolVersion = 0, string? TypeMapHash = null)
    {
    }
}
