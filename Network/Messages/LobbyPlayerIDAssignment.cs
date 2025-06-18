using FDG;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FutureOfDarkGrimness.Network.Messages
{
    /// <summary>
    /// Message sent to a new client to inform them of what their PlayerID is.
    /// </summary>
    public record LobbyPlayerIDAssignment(PlayerID playerID);
}
