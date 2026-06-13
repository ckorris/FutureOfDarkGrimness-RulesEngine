using FDG;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Messages
{
    /// <summary>
    /// Sent when the client first connects to the host to inform the client what their Player ID is.
    /// This may need to change if we ever allow a combined hot seat and networked game, such that
    /// a remote device can have multiple players on it.
    /// </summary>
    /// <param name="playerID">ID assigned to the newly-connected player.</param>
    public record LobbyPlayerIDAssignment(PlayerID playerID);
}
