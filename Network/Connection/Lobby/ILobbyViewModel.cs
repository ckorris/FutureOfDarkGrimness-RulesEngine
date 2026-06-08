using FDG.EngineInterface;
using FDG.Network.Messages;
using FDG.SaveLoad;

namespace FDG.Network.Connection.Lobby
{
    public interface ILobbyViewModel : IDisposable
    {
        bool HasHostPrivileges { get; }

        /// <summary>
        /// True when this side owns the authoritative game state and can produce a save (host only;
        /// see work item #054 for client-initiated saving).
        /// </summary>
        bool CanSaveGame { get; }

        /// <summary>
        /// Serializes the current in-progress game to a save string (see
        /// <see cref="SaveLoad.GameSaveSerializer"/>), or null if this side can't save. The caller
        /// writes it to a <c>.fdgsave</c> file.
        /// </summary>
        string? SaveGameToJson();

        /// <summary>
        /// The first parameter is the instance that you can use to bind your entire game view to.
        /// The second is a request to provide a reference to a <see cref="StageResolution.StageResolverRegistry"/>, 
        /// and you must fulfill this action before you can play the game.
        /// </summary>
        event Action<IFDGGame>? OnLaunched; //Need arguments?

        IObservable<string> ServerNameObservable { get; }

        IObservable<LobbyChatMessage> ChatMessagesObservable { get; }

        IObservable<IReadOnlyList<LobbyPlayerInfoSummary>> PlayerInfosObservable { get; }

        IObservable<int> ArmyPointsObservable { get; }
        IObservable<int> TerrainPieceCountObservable { get; }
        IObservable<ETerrainPlacementMode> TerrainPlacementModeObservable { get; }
        IObservable<string?> TerrainLayoutPathObservable { get; }
        IObservable<ERandomnessType> RandomnessTypeObservable { get; }
        IObservable<ETurnStyle> TurnStyleObservable { get; }

        string ServerName { get; }

        IReadOnlyList<LobbyChatMessage> ChatMessages { get; }

        IReadOnlyList<LobbyPlayerInfoSummary> PlayerInfos { get; }

        int ArmyPoints { get; }

        int TerrainCount { get; }

        ETerrainPlacementMode TerrainPlacementMode { get; }

        string? TerrainLayoutPath { get; }

        ERandomnessType RandomnessType { get; }

        ETurnStyle TurnStyle { get; }

        bool CheckCanModifyPlayerIDInfo(PlayerID playerID);

        void AddLocalPlayer();

        void AddAiPlayer();

        void SendMessage(string message);

        void UpdateArmyListFile(PlayerID playerId, ArmyListFile armyListFile);

        void SetArmyPoints(int armyPoints);

        void SetTerrainCount(int terrainCount);

        void SetTerrainPlacementMode(ETerrainPlacementMode mode);

        void SetTerrainLayoutPath(string? path);

        void SetRandomnessType(ERandomnessType randomnessType);

        void SetTurnStyle(ETurnStyle turnStyle);

        bool TryLaunchGame(out string? failReason);
    }
}
