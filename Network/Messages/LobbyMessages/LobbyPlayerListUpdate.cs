using FDG.Network.Connection.Lobby;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Messages
{
    internal class LobbyPlayerListUpdate
    {
        public List<LobbyPlayerInfoSummary> PlayerInfoList;

        public LobbyPlayerListUpdate(List<LobbyPlayerInfoSummary> playerInfoList)
        {
            PlayerInfoList = playerInfoList;
        }
    }
}
