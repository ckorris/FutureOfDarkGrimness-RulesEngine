using FDG.Ai;
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

        /// <summary>Which AI plays this slot - meaningful only when <see cref="PlayerType"/> is AI (#191 A6).</summary>
        public EAiProfile AiProfile;

        public LobbyPlayerInfoFull(string playerName, ArmyListFile? armyListFile, ETeamOption teamNumber,
        EPlayerType playerType, ConnectionID connectionID, PlayerID playerID,
        EAiProfile aiProfile = EAiProfile.SoloRules)
        {
            PlayerName = playerName;
            ArmyListFile = armyListFile;
            TeamNumber = teamNumber;
            PlayerType = playerType;
            ConnectionID = connectionID;
            PlayerID = playerID;
            AiProfile = aiProfile;
        }
    }
}
