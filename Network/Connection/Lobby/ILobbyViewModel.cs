using FDG.Ai;
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
        IObservable<EObjectivePlacementMode> ObjectivePlacementModeObservable { get; }
        IObservable<ERandomnessType> RandomnessTypeObservable { get; }
        IObservable<ETurnStyle> TurnStyleObservable { get; }
        /// <summary>#201 cover proximity house rules toggle (default on) - see <see cref="GameSettings.CoverProximityExceptions"/>.</summary>
        IObservable<bool> CoverProximityExceptionsObservable { get; }
        /// <summary>#265 cosmetic table surface - see <see cref="GameSettings.TableBackground"/>.</summary>
        IObservable<ETableBackground> TableBackgroundObservable { get; }

        string ServerName { get; }

        IReadOnlyList<LobbyChatMessage> ChatMessages { get; }

        IReadOnlyList<LobbyPlayerInfoSummary> PlayerInfos { get; }

        int ArmyPoints { get; }

        int TerrainCount { get; }

        ETerrainPlacementMode TerrainPlacementMode { get; }

        string? TerrainLayoutPath { get; }

        EObjectivePlacementMode ObjectivePlacementMode { get; }

        ERandomnessType RandomnessType { get; }

        ETurnStyle TurnStyle { get; }

        bool CoverProximityExceptions { get; }

        ETableBackground TableBackground { get; }

        bool CheckCanModifyPlayerIDInfo(PlayerID playerID);

        void AddLocalPlayer();

        /// <summary>Adds an AI slot playing as <paramref name="profile"/> (#191 A6): the
        /// profile decides the resolver registry at launch and the bot's display name.</summary>
        void AddAiPlayer(EAiProfile profile);

        void SendMessage(string message);

        void UpdateArmyListFile(PlayerID playerId, ArmyListFile armyListFile);

        /// <summary>
        /// Sets a player's lobby colour pick (#221): an index into the front end's palette (opaque to the
        /// engine), or -1 to clear. On the host this applies directly (any row) and rebroadcasts the
        /// roster; on a client it sends the request to the host (own row only). The host ignores a pick
        /// another player already explicitly holds - the rebroadcast corrects the requester's view.
        /// </summary>
        void SetPlayerColor(PlayerID playerId, int colorIndex);

        /// <summary>
        /// Sets a player's lobby team (#255). Valid teams are 1..playerCount (as many teams as players).
        /// On the host this applies directly (any non-network row) and rebroadcasts the roster; on a
        /// client it sends the request to the host (own row only). Multiple players may share a team -
        /// that is the point - but launch is blocked while ALL players share one team. No-op in resume
        /// mode (saved games keep their saved teams).
        /// </summary>
        void SetPlayerTeam(PlayerID playerId, ETeamOption teamNumber);

        void SetArmyPoints(int armyPoints);

        void SetTerrainCount(int terrainCount);

        void SetTerrainPlacementMode(ETerrainPlacementMode mode);

        void SetTerrainLayoutPath(string? path);

        void SetObjectivePlacementMode(EObjectivePlacementMode mode);

        void SetRandomnessType(ERandomnessType randomnessType);

        void SetTurnStyle(ETurnStyle turnStyle);

        void SetCoverProximityExceptions(bool enabled);

        void SetTableBackground(ETableBackground background);

        bool TryLaunchGame(out string? failReason);

        /// <summary>#153 launch gate (decision 9): hard legality problems in the players' loaded armies —
        /// over the lobby points limit, plus full catalog validation Errors for Forge-built armies (which
        /// carry their book + selections). The UI shows these in a "launch anyway?" confirm before
        /// <see cref="TryLaunchGame"/>; empty means clean. Host-side only — a client returns empty (only
        /// the host can launch).</summary>
        IReadOnlyList<string> ValidateArmiesForLaunch();

        /// <summary>True when this lobby was created from a saved game and resumes instead of starting fresh.</summary>
        bool IsResumeMode { get; }

        /// <summary>Re-crews a saved slot (by its preserved PlayerID) before resuming. No-op when not host/resume.</summary>
        /// <param name="aiProfile">Which AI plays the slot when <paramref name="playerType"/> is AI.</param>
        void SetSavedSlotPlayerType(PlayerID slotPlayerID, EPlayerType playerType,
            EAiProfile aiProfile = EAiProfile.SoloRules);

        /// <summary>Resumes the loaded game (host only). Returns false with a reason if this isn't a resume lobby.</summary>
        bool TryResumeGame(out string? failReason);
    }
}
