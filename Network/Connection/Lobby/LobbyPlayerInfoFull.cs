using FDG.Players;
using FDG.SaveLoad;

namespace FDG.Network.Connection.Lobby
{
    public class LobbyPlayerInfoFull
    {
        public string PlayerName;

        public ArmyListFile? ArmyListFile;

        public ETeamOption TeamNumber;

        public EPlayerType PlayerType;

        public ConnectionID ConnectionID;

        public PlayerID PlayerID;

        public LobbyPlayerInfoFull(string playerName, ArmyListFile? armyListFile, ETeamOption teamNumber,
        EPlayerType playerType, ConnectionID connectionID, PlayerID playerID)
        {
            PlayerName = playerName;
            ArmyListFile = armyListFile;
            TeamNumber = teamNumber;
            PlayerType = playerType;
            ConnectionID = connectionID;
            PlayerID = playerID;
        }
    }
}
