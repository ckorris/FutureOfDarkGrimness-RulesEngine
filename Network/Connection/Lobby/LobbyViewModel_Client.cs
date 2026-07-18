using FDG.GameModel;
using FDG;
using FDG.Data;
using FDG.EngineInterface;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using System.Diagnostics;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace FDG.Network.Connection.Lobby
{
    public class LobbyViewModel_Client : ILobbyViewModel
    {
        public bool HasHostPrivileges => false;

        // Clients don't hold the authoritative store; saving from a client is #054 (host produces it).
        public bool CanSaveGame => false;

        public string? SaveGameToJson() => null;

        // Only the host can load/resume a saved game.
        public bool IsResumeMode => false;

        public void SetSavedSlotPlayerType(PlayerID slotPlayerID, EPlayerType playerType,
            FDG.Ai.EAiProfile aiProfile = FDG.Ai.EAiProfile.SoloRules) { }

        public bool TryResumeGame(out string? failReason)
        {
            failReason = "Only the host can resume a saved game.";
            return false;
        }

        public IObservable<string> ServerNameObservable => _serverName;

        public IObservable<LobbyChatMessage> ChatMessagesObservable => _chatMessagesSubject;

        public IObservable<IReadOnlyList<LobbyPlayerInfoSummary>> PlayerInfosObservable => _playerInfos;

        public IObservable<int> ArmyPointsObservable => _settings_ArmyPoints;
        public IObservable<int> TerrainPieceCountObservable => _settings_TerrainPieceCount;
        public IObservable<ETerrainPlacementMode> TerrainPlacementModeObservable => _settings_TerrainPlacementMode;
        public IObservable<string?> TerrainLayoutPathObservable => _settings_TerrainLayoutPath;
        public IObservable<EObjectivePlacementMode> ObjectivePlacementModeObservable => _settings_ObjectivePlacementMode;
        public IObservable<ERandomnessType> RandomnessTypeObservable => _settings_RandomnessType;
        public IObservable<ETurnStyle> TurnStyleObservable => _settings_TurnMethod;

        public string ServerName => _serverName.Value;

        public IReadOnlyList<LobbyChatMessage> ChatMessages => _chatMessages;

        public IReadOnlyList<LobbyPlayerInfoSummary> PlayerInfos => _playerInfos.Value;

        public int ArmyPoints => _settings_ArmyPoints.Value;

        public int TerrainCount => _settings_TerrainPieceCount.Value;

        public ETerrainPlacementMode TerrainPlacementMode => _settings_TerrainPlacementMode.Value;

        public string? TerrainLayoutPath => _settings_TerrainLayoutPath.Value;

        public EObjectivePlacementMode ObjectivePlacementMode => _settings_ObjectivePlacementMode.Value;

        public ERandomnessType RandomnessType => _settings_RandomnessType.Value;

        public ETurnStyle TurnStyle => _settings_TurnMethod.Value;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessagesSubject;
        private readonly List<LobbyChatMessage> _chatMessages = new List<LobbyChatMessage>();

        private BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>> _playerInfos;

        private BehaviorSubject<int> _settings_ArmyPoints;
        private BehaviorSubject<int> _settings_TerrainPieceCount;
        private BehaviorSubject<ETerrainPlacementMode> _settings_TerrainPlacementMode;
        private BehaviorSubject<string?> _settings_TerrainLayoutPath;
        private BehaviorSubject<EObjectivePlacementMode> _settings_ObjectivePlacementMode;
        private BehaviorSubject<ERandomnessType> _settings_RandomnessType;
        private BehaviorSubject<ETurnStyle> _settings_TurnMethod;

        private PlayerID? _thisPlayerID = null;
        private string _thisPlayerName;

        IReadWriteableGameDataStore _gameDataStore;

        private IMessageBusClient _messageBusClient;

        // Kept so we can unsubscribe from OnDisconnected on dispose (QF8).
        private readonly INetworkClient _networkClient;

        // 0 until OnGameEnded has been raised once, then 1. Both a normal GameEndedMessage and a lost-host
        // disconnect can try to end the game; whichever wins, the other is suppressed so the client doesn't
        // fire the return-to-menu flow twice or overwrite a real result with "connection lost" (QF8).
        private int _gameEndedRaised;

        // Completes once the host accepts (result null) or rejects (result = readable reason) the join (#075).
        // ClientModal awaits this before navigating to the lobby, so a build-mismatch rejection shows in the
        // connect modal instead of half-joining.
        private readonly TaskCompletionSource<string?> _joinResult =
            new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> JoinResultTask => _joinResult.Task;


        private const string SERVER_JOIN_MESSAGE = "Welcome to the server.";

        public event Action<IFDGGame>? OnLaunched;

        // Raised when the host broadcasts a GameEndedMessage (work item #040). A non-host client has no
        // FDGServer, so this wire message is its game-end signal — it drives the same return-to-menu flow
        // as the host. Fires on the network read-loop thread.
        public event Action<string>? OnGameEnded;

        public LobbyViewModel_Client(string thisPlayerName, INetworkClient networkclient, string? password = null)
        {
             _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            _messageBusClient = new MessageBusClient_Networked(networkclient, _gameDataStore);
            _networkClient = networkclient;
            _networkClient.OnDisconnected += OnHostConnectionLost;

            _thisPlayerName = thisPlayerName;

            _serverName = new BehaviorSubject<string>("");
            _chatMessagesSubject = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(0);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(0);
            _settings_TerrainPlacementMode = new BehaviorSubject<ETerrainPlacementMode>(ETerrainPlacementMode.AutoFromLayout);
            _settings_TerrainLayoutPath = new BehaviorSubject<string?>(null);
            _settings_ObjectivePlacementMode = new BehaviorSubject<EObjectivePlacementMode>(EObjectivePlacementMode.AutoPlaced);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(ERandomnessType.Realistic);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(ETurnStyle.Standard);

            //Init empty player list. The host should update us.
            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>>(new List<LobbyPlayerInfoSummary>());

            _messageBusClient.RegisterForMessageEvent<LobbyPlayerIDAssignment>(OnPlayerIDAssignmentReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyServerNameMessage>(OnServerNameUpdateReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyPlayerListUpdate>(OnPlayerListUpdateReceived);
            _messageBusClient.RegisterForMessageEvent<LaunchGameMessage>(OnLaunchGameMessageReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyGameSettingsUpdate>(OnGameSettingsUpdateReceived);
            _messageBusClient.RegisterForMessageEvent<GameEndedMessage>(OnGameEndedMessageReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyJoinRejectedMessage>(OnJoinRejectedReceived);

            //Send greeting — includes this build's protocol version + type-map fingerprint so the host can
            //refuse an incompatible build before we join the roster (#075), plus the lobby password the host
            //gates the join on (QF1).
            NewLobbyClientGreeting greeting = new NewLobbyClientGreeting(
                thisPlayerName, NetworkProtocol.Version, NetworkProtocol.LocalTypeMapHash, password);
            _messageBusClient.SendCommandToHostAsync(greeting);

            //Show init message in chatbox.
            AddMessageToLocalList(new LobbyChatMessage("System", SERVER_JOIN_MESSAGE));
        }

        public void AddLocalPlayer()
        {
            throw new InvalidOperationException("Tried to add local player when not the host.");
        }

        public void AddAiPlayer(FDG.Ai.EAiProfile profile)
        {
            throw new InvalidOperationException("Tried to add AI player when not the host.");
        }

        public void SendMessage(string message)
        {
            Debug.WriteLine($"Sending message: {message}");

            LobbyChatMessage_FromClient chatMessage = new LobbyChatMessage_FromClient(_thisPlayerName, message);

            _messageBusClient.SendCommandToHostAsync(chatMessage);
        }

        private void AddMessageToLocalList(LobbyChatMessage chatMessage)
        {
            _chatMessagesSubject.OnNext(chatMessage);
            _chatMessages.Add(chatMessage);
        }

        public bool TryLaunchGame(out string? failReason)
        {
            failReason = "Can't launch the game as the client.";
            return false;
        }

        // Only the host can launch, so the client never has a launch gate to show.
        public IReadOnlyList<string> ValidateArmiesForLaunch() => Array.Empty<string>();

        public void Dispose()
        {
            _messageBusClient.DeregisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _networkClient.OnDisconnected -= OnHostConnectionLost;
        }

        // The host's connection dropped (crash / quit / network loss). Treat it as a game-end so the client
        // leaves the frozen board (or dead lobby) and returns to the menu, rather than hanging (QF8).
        private void OnHostConnectionLost()
        {
            RaiseGameEndedOnce("Connection to the host was lost.");
        }

        // Raises OnGameEnded at most once, so a normal end and a subsequent host-gone disconnect don't both
        // fire (QF8).
        private void RaiseGameEndedOnce(string result)
        {
            if (Interlocked.Exchange(ref _gameEndedRaised, 1) != 0)
            {
                return;
            }

            OnGameEnded?.Invoke(result);
        }

        private void OnPlayerIDAssignmentReceived(LobbyPlayerIDAssignment assignment)
        {
            _thisPlayerID = assignment.playerID;
            _joinResult.TrySetResult(null); //Host accepted: a PlayerID assignment is the success signal (#075).
        }

        private void OnJoinRejectedReceived(LobbyJoinRejectedMessage message)
        {
            Debug.WriteLine($"Join rejected by host: {message.Reason}");
            _joinResult.TrySetResult(message.Reason); //Build mismatch — surface the reason, don't join (#075).
        }

        private void OnChatMessageReceived(LobbyChatMessage message)
        {
            Debug.WriteLine($"Received chat message as client: {message.Message}");

            AddMessageToLocalList(message);
        }

        private void OnServerNameUpdateReceived(LobbyServerNameMessage lobbyServerNameMessage)
        {
            _serverName.OnNext(lobbyServerNameMessage.ServerName);
        }

        private void OnPlayerListUpdateReceived(LobbyPlayerListUpdate playerListUpdate)
        {
            _playerInfos.OnNext(playerListUpdate.PlayerInfoList);
        }

        private void OnGameSettingsUpdateReceived(LobbyGameSettingsUpdate gameSettingsUpdate)
        {
            if (_settings_ArmyPoints.Value != gameSettingsUpdate.GameSettings.ArmyPoints)
            {
                _settings_ArmyPoints.OnNext(gameSettingsUpdate.GameSettings.ArmyPoints);
            }
            if (_settings_TerrainPieceCount.Value != gameSettingsUpdate.GameSettings.TerrainPieceCount)
            {
                _settings_TerrainPieceCount.OnNext(gameSettingsUpdate.GameSettings.TerrainPieceCount);
            }
            if (_settings_TerrainPlacementMode.Value != gameSettingsUpdate.GameSettings.TerrainPlacementMode)
            {
                _settings_TerrainPlacementMode.OnNext(gameSettingsUpdate.GameSettings.TerrainPlacementMode);
            }
            if (_settings_TerrainLayoutPath.Value != gameSettingsUpdate.GameSettings.TerrainLayoutPath)
            {
                _settings_TerrainLayoutPath.OnNext(gameSettingsUpdate.GameSettings.TerrainLayoutPath);
            }
            if (_settings_ObjectivePlacementMode.Value != gameSettingsUpdate.GameSettings.ObjectivePlacementMode)
            {
                _settings_ObjectivePlacementMode.OnNext(gameSettingsUpdate.GameSettings.ObjectivePlacementMode);
            }
            if (_settings_RandomnessType.Value != gameSettingsUpdate.GameSettings.RandomnessType)
            {
                _settings_RandomnessType.OnNext(gameSettingsUpdate.GameSettings.RandomnessType);
            }
            if (_settings_TurnMethod.Value != gameSettingsUpdate.GameSettings.TurnStyle)
            {
                _settings_TurnMethod.OnNext(gameSettingsUpdate.GameSettings.TurnStyle);
            }
        }

        private void OnLaunchGameMessageReceived(LaunchGameMessage launchGameMessage)
        {
            Debug.WriteLine($"Received launch game message. ");

            if(_thisPlayerID.HasValue == false)
            {
                throw new InvalidOperationException("Tried to launch game without a PlayerID being assigned.");
            }

            FDGGame_AsClient fdgGame = new FDGGame_AsClient(_gameDataStore, _messageBusClient, _thisPlayerID.Value);

            OnLaunched?.Invoke(fdgGame);
        }

        private void OnGameEndedMessageReceived(GameEndedMessage gameEndedMessage)
        {
            RaiseGameEndedOnce(gameEndedMessage.Result);
        }

        public void SetArmyPoints(int armyPoints)
        {
            throw new InvalidOperationException("Tried to set army points when not the host.");
        }

        public void SetTerrainCount(int terrainCount)
        {
            throw new InvalidOperationException("Tried to set terrain count when not the host.");

        }

        public void SetTerrainPlacementMode(ETerrainPlacementMode mode)
        {
            throw new InvalidOperationException("Tried to set terrain placement mode when not the host.");
        }

        public void SetObjectivePlacementMode(EObjectivePlacementMode mode)
        {
            throw new InvalidOperationException("Tried to set objective placement mode when not the host.");
        }

        public void SetTerrainLayoutPath(string? path)
        {
            throw new InvalidOperationException("Tried to set terrain layout path when not the host.");
        }

        public void SetRandomnessType(ERandomnessType randomnessType)
        {
            throw new InvalidOperationException("Tried to set randomness type when not the host.");

        }

        public void SetTurnStyle(ETurnStyle turnStyle)
        {
            throw new InvalidOperationException("Tried to set turn style when not the host.");

        }

        public bool CheckCanModifyPlayerIDInfo(PlayerID playerID)
        {
            //If we haven't been assigned a value yet, assume no.
            return _thisPlayerID.HasValue && _thisPlayerID.Value == playerID;
        }

        public void UpdateArmyListFile(PlayerID playerId, ArmyListFile armyListFile)
        {
            if(_thisPlayerID.HasValue == false)
            {
                throw new Exception($"Tried to update army list before we've been assigned an ID.");
            }

            if(playerId !=  _thisPlayerID.Value)
            {
                throw new InvalidOperationException("Client to update an army list for the wrong player. " +
                    $"Client's ID: {_thisPlayerID.Value} Update attempt ID: {playerId}");
            }

            ArmyListUpdateMessage message = ArmyListUpdateMessage.FromArmy(playerId, armyListFile);

            _messageBusClient.SendCommandToHostAsync(message);
        }

        // #221: request a colour pick from the host, own row only (mirrors UpdateArmyListFile). The host
        // applies it (unless another player already holds the colour) and the roster rebroadcast carries
        // the result back - there's no optimistic local update.
        public void SetPlayerColor(PlayerID playerId, int colorIndex)
        {
            if (_thisPlayerID.HasValue == false)
            {
                throw new Exception("Tried to set a player colour before we've been assigned an ID.");
            }

            if (playerId != _thisPlayerID.Value)
            {
                throw new InvalidOperationException("Client tried to set a colour for the wrong player. " +
                    $"Client's ID: {_thisPlayerID.Value} Update attempt ID: {playerId}");
            }

            _messageBusClient.SendCommandToHostAsync(new PlayerColorUpdateMessage(playerId, colorIndex));
        }
    }
}
