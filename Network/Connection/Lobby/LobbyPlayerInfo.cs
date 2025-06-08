using FDG.Players;
using FutureOfDarkGrimness.Players;

namespace FDG.Network.Connection.Lobby
{
    public record LobbyPlayerInfo(string PlayerName, ArmyListSummary ArmyListSummary, ETeamOption TeamNumber, 
        EPlayerType PlayerType, ConnectionID connectionID);

    
}
