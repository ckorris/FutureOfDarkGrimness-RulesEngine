using FDG.EngineInterface;
using FDG.Network.Messages;
using FDG.Players;
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

        /// <summary>
        /// Raised when the game finishes, carrying the result string (e.g. "Player X wins!" / "It's a
        /// tie!"). Forwarded from <see cref="GameModel.FDGServer.OnGameEnded"/> on the host so the front
        /// end can offer a return-to-menu. Fires on the engine thread.
        ///
        /// Only the host raises this today — the host owns the authoritative game state and its
        /// FDGServer. A non-host networked client has no clean game-end signal yet (it only sees the
        /// replicated "wins!" banner beat); wiring that is deferred to work item #040's client follow-up.
        /// </summary>
        event Action<string>? OnGameEnded;

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

        /// <summary>True when this lobby was created from a saved game and resumes instead of starting fresh.</summary>
        bool IsResumeMode { get; }

        /// <summary>Re-crews a saved slot (by its preserved PlayerID) before resuming. No-op when not host/resume.</summary>
        void SetSavedSlotPlayerType(PlayerID slotPlayerID, EPlayerType playerType);

        /// <summary>Resumes the loaded game (host only). Returns false with a reason if this isn't a resume lobby.</summary>
        bool TryResumeGame(out string? failReason);
    }
}
