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
        public List<LobbyPlayerInfo> PlayerInfoList;

        public LobbyPlayerListUpdate(List<LobbyPlayerInfo> playerInfoList)
        {
            PlayerInfoList = playerInfoList;
        }
    }
}
