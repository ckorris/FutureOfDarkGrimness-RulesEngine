using FDG.Ai;
using FDG.Data;
using FDG.EngineInterface;
using FDG.GameModel;
using FDG.MessageBus;
using FDG.Network.Messages;
using FDG.Players;
using FDG.Presentation;
using FDG.SaveLoad;
using FDG.StageResolution;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace FDG.Network.Connection.Lobby
{
    public class LobbyViewModel_Host : ILobbyViewModel
    {
        public bool HasHostPrivileges => true;

        public bool CanSaveGame => true;

        // The host owns the authoritative store, so it serializes directly. Called from the UI thread
        // while the engine runs on another thread; in practice the player triggers a save while the
        // engine is awaiting their input (quiescent), and the snapshot iterates fixed-capacity stores
        // without resizing, so a torn read is unlikely. (#054 will move saving to a request the host
        // services at a guaranteed-safe point.)
        public string? SaveGameToJson() => GameSaveSerializer.Save((GameDataStore)_gameDataStore);

        public IObservable<string> ServerNameObservable => _serverName;

        public IObservable<LobbyChatMessage> ChatMessagesObservable => _chatMessagesSubject;

        public IObservable<IReadOnlyList<LobbyPlayerInfoSummary>> PlayerInfosObservable => _playerInfos;

        public IObservable<int> ArmyPointsObservable => _settings_ArmyPoints;
        public IObservable<int> TerrainPieceCountObservable => _settings_TerrainPieceCount;
        public IObservable<int> TerrainPointsTotalObservable => _settings_TerrainPointsTotal;
        public IObservable<int> TerrainPointsPerTurnObservable => _settings_TerrainPointsPerTurn;
        public IObservable<ETerrainPlacementMode> TerrainPlacementModeObservable => _settings_TerrainPlacementMode;
        public IObservable<string?> TerrainLayoutPathObservable => _settings_TerrainLayoutPath;
        public IObservable<EObjectivePlacementMode> ObjectivePlacementModeObservable => _settings_ObjectivePlacementMode;
        public IObservable<ERandomnessType> RandomnessTypeObservable => _settings_RandomnessType;
        public IObservable<ETurnStyle> TurnStyleObservable => _settings_TurnMethod;
        public IObservable<bool> CoverProximityExceptionsObservable => _settings_CoverProximityExceptions;
        public IObservable<ETableBackground> TableBackgroundObservable => _settings_TableBackground;

        public string ServerName => _serverName.Value;

        public IReadOnlyList<LobbyChatMessage> ChatMessages => _chatMessages;

        public IReadOnlyList<LobbyPlayerInfoSummary> PlayerInfos => _playerInfos.Value;

        public int ArmyPoints => _settings_ArmyPoints.Value;

        public int TerrainCount => _settings_TerrainPieceCount.Value;

        public int TerrainPointsTotal => _settings_TerrainPointsTotal.Value;

        public int TerrainPointsPerTurn => _settings_TerrainPointsPerTurn.Value;

        public ETerrainPlacementMode TerrainPlacementMode => _settings_TerrainPlacementMode.Value;

        public string? TerrainLayoutPath => _settings_TerrainLayoutPath.Value;

        public EObjectivePlacementMode ObjectivePlacementMode => _settings_ObjectivePlacementMode.Value;

        public ERandomnessType RandomnessType => _settings_RandomnessType.Value;

        public ETurnStyle TurnStyle => _settings_TurnMethod.Value;

        public bool CoverProximityExceptions => _settings_CoverProximityExceptions.Value;

        public ETableBackground TableBackground => _settings_TableBackground.Value;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessagesSubject;
        private readonly List<LobbyChatMessage> _chatMessages = new List<LobbyChatMessage>();

        private BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>> _playerInfos;
        private Dictionary<PlayerID, LobbyPlayerInfoFull> _playerInfosFull = new Dictionary<PlayerID, LobbyPlayerInfoFull>();

        private BehaviorSubject<int> _settings_ArmyPoints;
        private BehaviorSubject<int> _settings_TerrainPieceCount;
        private BehaviorSubject<int> _settings_TerrainPointsTotal;
        private BehaviorSubject<int> _settings_TerrainPointsPerTurn;
        private BehaviorSubject<ETerrainPlacementMode> _settings_TerrainPlacementMode;
        private BehaviorSubject<string?> _settings_TerrainLayoutPath;
        private BehaviorSubject<EObjectivePlacementMode> _settings_ObjectivePlacementMode;
        private BehaviorSubject<ERandomnessType> _settings_RandomnessType;
        private BehaviorSubject<ETurnStyle> _settings_TurnMethod;
        private BehaviorSubject<bool> _settings_CoverProximityExceptions;
        private BehaviorSubject<ETableBackground> _settings_TableBackground;

        private INetworkHost _host;

        // The lobby password the host set, or null/empty for an open lobby. Compared plain-text against the
        // joining client's greeting (QF1) - adequate at this trust level (a small group of invited players);
        // the real confidentiality story is running over a trusted tunnel (see the build's Tailscale note).
        private readonly string? _password;

        // How long a freshly accepted connection has to send its greeting before the host drops it (QF2).
        // Guards an internet-exposed port: a scanner or half-open connection that never greets is evicted
        // instead of sitting on the roster's broadcast stream.
        private const int GREETING_TIMEOUT_MS = 10000;

        private string _hostPlayerName;

        private MessageBusHost_Networked _messageBus;

        private const string SERVER_START_MESSAGE = "Server started successfully.";
        private const string LAUNCHING_GAME_MESSAGE = "About to launch game.";

        private static readonly Random _tributeRng = new Random();

        // Bot banter: a bot posts one line to lobby chat a few seconds after it is added, so a lobby
        // with bots in it feels inhabited while everyone waits on the host. Cosmetic and off the game
        // path, so these draw from Random.Shared rather than the seeded GameRandom - nothing here can
        // reach a rules outcome. ASCII only (the ImGui atlas bakes no glyph above U+00FF).
        private static readonly string[] BOT_GREETINGS = new[]
        {
            "May your energy sword chip and shatter.",
            "Resistors are futile.",
            "Only humanoid.",
            "Quite an experience to live in Fear(2), isn't it?",
            "Today is a good day to dice.",
            "I swear by my pretty floral bonnet, I will beat you.",
            "You have no chance to survive. Make your time.",
            "The odds of you surviving are approximately one thousand three hundred thirty seven to one.",
            "Shall we play a game of tabletop war?",
            "Sometimes, you have to roll the hard D6.",
        };

        // Which lines are already spoken, so no two bots pick the same one. Guarded by its own lock:
        // every bot's greeting resolves on its own thread-pool continuation, so the picks genuinely race.
        private readonly HashSet<int> _usedBotGreetings = new HashSet<int>();
        private readonly object _botGreetingLock = new object();

        // How long after being added a bot waits before speaking. Instance fields rather than consts
        // purely so tests can collapse the wait instead of sleeping for whole seconds.
        internal int BotGreetingMinDelayMs = 3000;
        internal int BotGreetingMaxDelayMs = 5000;

        public event Action<IFDGGame>? OnLaunched;

        public event Action<string>? OnGameEnded;

        public event Action<GameResult>? OnGameCompleted;

        private GameSettings _gameSettings = GameSettings.GetDefault();

        IReadWriteableGameDataStore _gameDataStore;

        private readonly bool _isResume;

        public bool IsResumeMode => _isResume;

        // Set once the game launches (QF6). After this a fresh greeting is refused - the roster is fixed and
        // the store is mid-game, so a late joiner has nothing valid to become.
        private bool _isLaunched;

        // Dispose is reached from several app paths (lobby back, game exit, view-model replacement), so it
        // must be idempotent (#279).
        private bool _isDisposed;

        public LobbyViewModel_Host(string hostPlayerName, string serverName, string? password, INetworkHost host)
        {
            _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            _messageBus = new MessageBusHost_Networked(host, _gameDataStore);
            _host = host;
            _password = password;
            _hostPlayerName = hostPlayerName;
            PlayerID thisPlayerID = new PlayerID(Guid.NewGuid());

            _serverName = new BehaviorSubject<string>(serverName);
            _chatMessagesSubject = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(_gameSettings.ArmyPoints);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(_gameSettings.TerrainPieceCount);
            _settings_TerrainPointsTotal = new BehaviorSubject<int>(_gameSettings.TerrainPointsTotal);
            _settings_TerrainPointsPerTurn = new BehaviorSubject<int>(_gameSettings.TerrainPointsPerTurn);
            _settings_TerrainPlacementMode = new BehaviorSubject<ETerrainPlacementMode>(_gameSettings.TerrainPlacementMode);
            _settings_TerrainLayoutPath = new BehaviorSubject<string?>(_gameSettings.TerrainLayoutPath);
            _settings_ObjectivePlacementMode = new BehaviorSubject<EObjectivePlacementMode>(_gameSettings.ObjectivePlacementMode);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(_gameSettings.RandomnessType);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(_gameSettings.TurnStyle);
            _settings_CoverProximityExceptions = new BehaviorSubject<bool>(_gameSettings.CoverProximityExceptionsEnabled);
            _settings_TableBackground = new BehaviorSubject<ETableBackground>(_gameSettings.TableBackground);

            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>>(new List<LobbyPlayerInfoSummary>());

            host.OnNewClientConnected += OnNewClientConnected;
            host.OnClientDisconnected += OnClientDisconnected;

            _messageBus.RegisterForMessageEvent<LobbyChatMessage>(OnLocalChatMessageReceived);
            _messageBus.RegisterForMessageEvent<LobbyChatMessage_FromClient>(OnChatMessageReceived);
            _messageBus.RegisterForConnectionMessageEvent<NewLobbyClientGreeting>(OnReceiveNewClientGreeting);
            _messageBus.RegisterForMessageEvent<ArmyListUpdateMessage>(OnArmyListFileUpdateReceived);
            _messageBus.RegisterForMessageEvent<PlayerColorUpdateMessage>(OnPlayerColorUpdateReceived);
            _messageBus.RegisterForMessageEvent<PlayerTeamUpdateMessage>(OnPlayerTeamUpdateReceived);

            //Show init message in chatbox.
            AddMessageToLocalList(new LobbyChatMessage("System", SERVER_START_MESSAGE));

            //First init just ourselves.
            //ArmyListSummary tempSummary = new ArmyListSummary("Manhandlers", "Battle Brothers", 2000);

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(hostPlayerName, null, ETeamOption.Team1,
                EPlayerType.Local, new ConnectionID(Guid.Empty), thisPlayerID);
            _playerInfosFull.Add(thisPlayerID, newLobbyPlayerInfo);
            UpdateInfoSummariesFromFullList();
        }

        /// <summary>
        /// Resume constructor (work item #052): builds a host lobby around a LOADED game. The store
        /// already holds the world + <see cref="GameProgressData"/>; the lobby's slot list is seeded
        /// from the saved <see cref="PlayerSlotInfo"/> entries, preserving the saved PlayerIDs, so the
        /// host can re-crew each slot (default: host plays the first slot, the rest become AI) before
        /// resuming. Networked re-crew (assigning connected clients to saved slots) is a follow-up.
        /// </summary>
        public LobbyViewModel_Host(string hostPlayerName, string serverName, string? password, INetworkHost host,
            GameDataStore loadedGameDataStore)
        {
            _isResume = true;
            _gameDataStore = loadedGameDataStore;
            _messageBus = new MessageBusHost_Networked(host, _gameDataStore);
            _host = host;
            _password = password;
            _hostPlayerName = hostPlayerName;

            // Faithfully resume the saved game's settings (turn style, randomness, etc.).
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(loadedGameDataStore);
            _gameSettings = progress?.Settings ?? GameSettings.GetDefault();

            _serverName = new BehaviorSubject<string>(serverName);
            _chatMessagesSubject = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(_gameSettings.ArmyPoints);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(_gameSettings.TerrainPieceCount);
            _settings_TerrainPointsTotal = new BehaviorSubject<int>(_gameSettings.TerrainPointsTotal);
            _settings_TerrainPointsPerTurn = new BehaviorSubject<int>(_gameSettings.TerrainPointsPerTurn);
            _settings_TerrainPlacementMode = new BehaviorSubject<ETerrainPlacementMode>(_gameSettings.TerrainPlacementMode);
            _settings_TerrainLayoutPath = new BehaviorSubject<string?>(_gameSettings.TerrainLayoutPath);
            _settings_ObjectivePlacementMode = new BehaviorSubject<EObjectivePlacementMode>(_gameSettings.ObjectivePlacementMode);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(_gameSettings.RandomnessType);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(_gameSettings.TurnStyle);
            _settings_CoverProximityExceptions = new BehaviorSubject<bool>(_gameSettings.CoverProximityExceptionsEnabled);
            _settings_TableBackground = new BehaviorSubject<ETableBackground>(_gameSettings.TableBackground);

            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>>(new List<LobbyPlayerInfoSummary>());

            host.OnNewClientConnected += OnNewClientConnected;
            host.OnClientDisconnected += OnClientDisconnected;

            _messageBus.RegisterForMessageEvent<LobbyChatMessage>(OnLocalChatMessageReceived);
            _messageBus.RegisterForMessageEvent<LobbyChatMessage_FromClient>(OnChatMessageReceived);
            _messageBus.RegisterForConnectionMessageEvent<NewLobbyClientGreeting>(OnReceiveNewClientGreeting);
            _messageBus.RegisterForMessageEvent<ArmyListUpdateMessage>(OnArmyListFileUpdateReceived);
            _messageBus.RegisterForMessageEvent<PlayerColorUpdateMessage>(OnPlayerColorUpdateReceived);
            _messageBus.RegisterForMessageEvent<PlayerTeamUpdateMessage>(OnPlayerTeamUpdateReceived);

            AddMessageToLocalList(new LobbyChatMessage("System", "Loaded saved game. Assign players to slots, then Resume."));

            SeedSlotsFromSave(loadedGameDataStore, hostPlayerName);
            UpdateInfoSummariesFromFullList();
        }

        // Recreate the lobby's slot list from the saved PlayerSlotInfo entries, reusing the saved
        // PlayerIDs (no remap needed). Default re-crew: the host adopts the lowest-numbered slot
        // (Local), the rest become AI; the host can change each slot's controller before resuming.
        private void SeedSlotsFromSave(IReadWriteableGameDataStore store, string hostPlayerName)
        {
            List<PlayerSlotInfo> savedSlots = store.GetAllValues<PlayerSlotInfo>().OrderBy(s => s.SlotID).ToList();

            bool hostAssigned = false;
            foreach (PlayerSlotInfo slot in savedSlots)
            {
                EPlayerType type = hostAssigned ? EPlayerType.AI : EPlayerType.Local;
                string name = hostAssigned ? slot.Name : hostPlayerName;
                hostAssigned = true;

                LobbyPlayerInfoFull info = new LobbyPlayerInfoFull(name, null, (ETeamOption)slot.TeamNumber,
                    type, new ConnectionID(Guid.Empty), slot.PlayerID);
                _playerInfosFull[slot.PlayerID] = info;
            }
        }

        public void SendMessage(string message)
        {
            LobbyChatMessage chatMessage = new LobbyChatMessage(_hostPlayerName, message);
            _messageBus.SendCommandToAllAsync(chatMessage);
        }

        private void AddMessageToLocalList(LobbyChatMessage chatMessage)
        {
            _chatMessagesSubject.OnNext(chatMessage);
            _chatMessages.Add(chatMessage);
        }

        private void OnNewClientConnected(ConnectionID connectionID)
        {
            Debug.WriteLine($"{nameof(LobbyViewModel_Host)}.{nameof(OnNewClientConnected)}.");

            // Evict a connection that never greets within the timeout (QF2). A real client greets
            // immediately on construction, so anything still un-rostered after the window is a scan or a
            // half-open connection that shouldn't keep receiving broadcasts.
            _ = EnforceGreetingTimeoutAsync(connectionID);
        }

        private async Task EnforceGreetingTimeoutAsync(ConnectionID connectionID)
        {
            await Task.Delay(GREETING_TIMEOUT_MS).ConfigureAwait(false);

            try
            {
                // A greeted client (new-game or resume) is in _playerInfosFull with this ConnectionID; a
                // scanner never made it onto the roster.
                bool hasRosterEntry = _playerInfosFull.Values.Any(info => info.ConnectionID == connectionID);
                if (hasRosterEntry == false)
                {
                    Debug.WriteLine($"Dropping connection {connectionID.ID}: no greeting within timeout.");
                    _host.DisconnectClient(connectionID);
                }
            }
            catch (Exception exception)
            {
                // Fire-and-forget: never let a snapshot race on _playerInfosFull surface as an unobserved
                // task exception. Worst case we skip evicting one connection.
                Debug.WriteLine($"Greeting-timeout check failed for {connectionID.ID}: {exception.Message}");
            }
        }

        // Reject a join: send the reason to just this connection, then drop it (QF1/QF2). Awaits the send so
        // the reason is handed to the socket before the close, so the client shows the reason rather than a
        // bare connection-closed.
        private async Task RejectJoinAsync(ConnectionID connectionID, string reason)
        {
            await _messageBus.SendCommandToSingleAsync(new LobbyJoinRejectedMessage(reason), connectionID)
                .ConfigureAwait(false);
            _host.DisconnectClient(connectionID);
        }

        private void OnReceiveNewClientGreeting(NewLobbyClientGreeting greeting, ConnectionID connectionID)
        {
            // Refuse a build-mismatched client before it joins the roster (#075): a drifted store type map
            // would silently corrupt replicated data (positional TypeIDs), and a wire-format change would
            // garble messages. The rejection goes only to the offending connection.
            if (!NetworkProtocol.TryValidateJoin(greeting.ProtocolVersion, greeting.TypeMapHash, out string rejectReason))
            {
                Debug.WriteLine($"Rejecting client '{greeting.PlayerName}': {rejectReason}");
                _ = RejectJoinAsync(connectionID, rejectReason);
                return;
            }

            // Gate the join on the lobby password (QF1). Plain-text compare is acceptable here (small invited
            // group; a trusted tunnel is the real confidentiality layer). An open lobby (null/empty password)
            // skips the check.
            if (!string.IsNullOrEmpty(_password) && greeting.Password != _password)
            {
                Debug.WriteLine($"Rejecting client '{greeting.PlayerName}': incorrect password.");
                _ = RejectJoinAsync(connectionID, "Incorrect password.");
                return;
            }

            // The game is already running - the roster is fixed and there's no valid slot for a latecomer,
            // so refuse the join rather than corrupt the in-progress game (QF6).
            if (_isLaunched)
            {
                Debug.WriteLine($"Rejecting client '{greeting.PlayerName}': game already in progress.");
                _ = RejectJoinAsync(connectionID, "Game already in progress.");
                return;
            }

            // Resume games have a fixed saved roster — route joining clients to saved slots instead of
            // creating new ones. Strictly isolated so the new-game path below is unchanged.
            if (_isResume)
            {
                OnResumeClientGreeting(greeting, connectionID);
                return;
            }

            Debug.WriteLine($"Received greeting from new client: {greeting.PlayerName}");

            // The greeting passed every gate — lift this connection's inbound frame cap to the full
            // size (#266) before it starts sending real payloads (army lists are legitimately large).
            _host.MarkClientAuthenticated(connectionID);

            //Assign a player ID to this person.
            //TODO: I may have decided to give players their IDs elsewhere but I can't remember, double check that.
            PlayerID newClientPlayerID = new PlayerID(Guid.NewGuid());
            // Send the ID assignment ONLY to the joining connection (QF5). Broadcasting it made every
            // already-connected client adopt the newest joiner's PlayerID (the client sets _thisPlayerID
            // unconditionally on receipt), so a second remote client silently hijacked the first's identity.
            _messageBus.SendCommandToSingleAsync(new LobbyPlayerIDAssignment(newClientPlayerID), connectionID);

            //Send the server name.
            LobbyServerNameMessage lobbyServerNameMessage = new LobbyServerNameMessage(_serverName.Value);
            _messageBus.SendCommandToAllAsync(lobbyServerNameMessage);

            //ArmyListSummary tempSummary = new ArmyListSummary("Knifeybois", "Alien Hives", 2000);

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(greeting.PlayerName, null, FirstEmptyTeam(),
                EPlayerType.Network, connectionID, newClientPlayerID);
            _playerInfosFull.Add(newClientPlayerID, newLobbyPlayerInfo);
            UpdateInfoSummariesFromFullList();

            LobbyGameSettingsUpdate gameSettingsUpdate = new LobbyGameSettingsUpdate(_gameSettings);
            _messageBus.SendCommandToAllAsync(gameSettingsUpdate);
        }

        // Resume mode: assign the joining client to the next saved slot still marked AI (slot order),
        // have it adopt that slot's SAVED PlayerID, and mark the slot Network. Reuses the saved
        // PlayerID — no remap. NOT yet live-tested: correct-by-design for 1v1 (one remote client);
        // multi-client relies on the same broadcast LobbyPlayerIDAssignment as the new-game flow.
        // Host-chosen connection→slot assignment (vs this auto-fill) is a future refinement.
        private void OnResumeClientGreeting(NewLobbyClientGreeting greeting, ConnectionID connectionID)
        {
            Debug.WriteLine($"[#052] Resume: greeting from {greeting.PlayerName}.");

            LobbyPlayerInfoFull? openSlot = _playerInfosFull.Values.FirstOrDefault(p => p.PlayerType == EPlayerType.AI);
            if (openSlot == null)
            {
                Debug.WriteLine("[#052] Resume: no open (AI) saved slot for the joining client.");
                return;
            }

            openSlot.PlayerType = EPlayerType.Network;
            openSlot.PlayerName = greeting.PlayerName;
            openSlot.ConnectionID = connectionID;

            // Accepted into a saved slot — lift the pre-auth frame cap (#266), as in the new-game path.
            _host.MarkClientAuthenticated(connectionID);

            // Adopt the saved slot's PlayerID (not a fresh one) so the client resumes as that player. Sent
            // only to the joining connection (QF5) - see the new-game path for why broadcasting is wrong.
            _messageBus.SendCommandToSingleAsync(new LobbyPlayerIDAssignment(openSlot.PlayerID), connectionID);
            _messageBus.SendCommandToAllAsync(new LobbyServerNameMessage(_serverName.Value));
            _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
            UpdateInfoSummariesFromFullList();
        }

        private void OnClientDisconnected(ConnectionID disconnectedConnectionID)
        {
            // A connection that dropped without ever making it onto the roster (a port scan, or a join we
            // rejected + disconnected) has no entry here; First would throw on that (QF7).
            KeyValuePair<PlayerID, LobbyPlayerInfoFull> entry =
                _playerInfosFull.FirstOrDefault(info => info.Value.ConnectionID == disconnectedConnectionID);
            if (entry.Value == null)
            {
                return;
            }

            _playerInfosFull.Remove(entry.Key);
            UpdateInfoSummariesFromFullList();
        }

        private void UpdateInfoSummariesFromFullList()
        {
            //Not exactly optimized, but done quite infrequently.
            List<LobbyPlayerInfoSummary> infoSummaries = new List<LobbyPlayerInfoSummary>();
            foreach(LobbyPlayerInfoFull fullInfo in _playerInfosFull.Values)
            {
                ArmyListSummary? summary = fullInfo.ArmyListFile != null
                    ? new ArmyListSummary(true, fullInfo.ArmyListFile.Name, fullInfo.ArmyListFile.Faction,
                    fullInfo.ArmyListFile.TotalPoints)
                    : new ArmyListSummary(false, "N/A", "N/A", 0);
                infoSummaries.Add(new LobbyPlayerInfoSummary(fullInfo.PlayerName, summary, fullInfo.TeamNumber, fullInfo.PlayerType,
                    fullInfo.ConnectionID, fullInfo.PlayerID, fullInfo.ColorIndex));
            }

            LobbyPlayerListUpdate playerListUpdateMessage = new LobbyPlayerListUpdate(infoSummaries);
            _messageBus.SendCommandToAllAsync(playerListUpdateMessage);

            _playerInfos.OnNext(infoSummaries);
        }

        private void OnChatMessageReceived(LobbyChatMessage_FromClient chatMessageClient)
        {
            Debug.WriteLine($"Received chat message as host: {chatMessageClient.Message}");

            LobbyChatMessage chatMessage = new LobbyChatMessage(chatMessageClient.SendingPlayerName, chatMessageClient.Message);

            //Relay it to everyone else.
            _messageBus.SendCommandToAllAsync(chatMessage); //TODO: Release the byte array but gotta be careful on timing.
        }

        private void OnLocalChatMessageReceived(LobbyChatMessage chatMessage)
        {
            //Put the chat message in our own box.
            AddMessageToLocalList(chatMessage);
        }

        private void OnArmyListFileUpdateReceived(ArmyListUpdateMessage armyUpdate)
        {
            UpdateArmyListFile(armyUpdate.PlayerID, armyUpdate.DecodeArmy());
        }

        // #221: a client's colour pick, applied through the same path the host's own picks take. Sender
        // ownership isn't validated (same trust level as OnArmyListFileUpdateReceived - see #186).
        private void OnPlayerColorUpdateReceived(PlayerColorUpdateMessage colorUpdate)
        {
            SetPlayerColor(colorUpdate.PlayerID, colorUpdate.ColorIndex);
        }

        public void SetPlayerColor(PlayerID playerId, int colorIndex)
        {
            if (_playerInfosFull.TryGetValue(playerId, out LobbyPlayerInfoFull? info) == false)
            {
                Debug.WriteLine($"SetPlayerColor: couldn't find player {playerId}.");
                return;
            }

            // First-committed-wins on a race for the same colour: an explicit pick another player already
            // holds is ignored, and the roster rebroadcast below never happens - the requester's UI stays
            // on the authoritative state it already has. (The palette is app-side; the engine only compares
            // picks as opaque ints, so front-end DEFAULT colours can't be reserved here - the front end's
            // dropdown enforces that side.)
            if (colorIndex >= 0 && _playerInfosFull.Values.Any(
                    other => other.PlayerID != playerId && other.ColorIndex == colorIndex))
            {
                Debug.WriteLine($"SetPlayerColor: colour {colorIndex} already held; ignoring pick by {playerId}.");
                return;
            }

            if (info.ColorIndex == colorIndex) return;

            info.ColorIndex = colorIndex;
            UpdateInfoSummariesFromFullList();
        }

        // #255: the default team for a newly added player - the lowest team number 1..N not currently
        // held, where N counts the new player (as many teams as players). A free slot always exists in
        // that range; at worst the new player lands on the team their own arrival created.
        private ETeamOption FirstEmptyTeam()
        {
            int newPlayerCount = _playerInfosFull.Count + 1;
            for (int team = 1; team <= newPlayerCount; team++)
            {
                if (_playerInfosFull.Values.All(info => (int)info.TeamNumber != team))
                    return (ETeamOption)team;
            }
            return (ETeamOption)newPlayerCount; //Unreachable: N existing players can't occupy all N+1 candidates.
        }

        // #255: a client's team pick, applied through the same path the host's own picks take. Sender
        // ownership isn't validated (same trust level as OnPlayerColorUpdateReceived - see #186).
        private void OnPlayerTeamUpdateReceived(PlayerTeamUpdateMessage teamUpdate)
        {
            SetPlayerTeam(teamUpdate.PlayerID, teamUpdate.TeamNumber);
        }

        public void SetPlayerTeam(PlayerID playerId, ETeamOption teamNumber)
        {
            // Resume lobbies keep their saved teams - editing them would desync the saved turn-order state.
            if (_isResume)
            {
                Debug.WriteLine("SetPlayerTeam: ignored in resume mode.");
                return;
            }

            if (_playerInfosFull.TryGetValue(playerId, out LobbyPlayerInfoFull? info) == false)
            {
                Debug.WriteLine($"SetPlayerTeam: couldn't find player {playerId}.");
                return;
            }

            // As many teams as players. (A player can hold a stale out-of-range team after someone
            // leaves - left as-is by design - but new picks stay in range.)
            int team = (int)teamNumber;
            if (team < 1 || team > _playerInfosFull.Count)
            {
                Debug.WriteLine($"SetPlayerTeam: team {team} out of range 1..{_playerInfosFull.Count}; ignoring.");
                return;
            }

            if (info.TeamNumber == teamNumber) return;

            info.TeamNumber = teamNumber;
            UpdateInfoSummariesFromFullList();
        }

        // Full network teardown (#279). Before this, Dispose only detached the chat relay: the greeting
        // handler stayed registered and the host kept listening, so an abandoned lobby kept accepting
        // joins as a half-alive zombie while the replacement lobby's FDGHost silently failed to bind the
        // port - the host UI showed nobody joining, client chat vanished (relay deregistered), and
        // never-removed roster entries piled up as ghost player slots.
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;

            _messageBus.DeregisterForMessageEvent<LobbyChatMessage>(OnLocalChatMessageReceived);
            _messageBus.DeregisterForMessageEvent<LobbyChatMessage_FromClient>(OnChatMessageReceived);
            _messageBus.DeregisterForConnectionMessageEvent<NewLobbyClientGreeting>(OnReceiveNewClientGreeting);
            _messageBus.DeregisterForMessageEvent<ArmyListUpdateMessage>(OnArmyListFileUpdateReceived);
            _messageBus.DeregisterForMessageEvent<PlayerColorUpdateMessage>(OnPlayerColorUpdateReceived);
            _messageBus.DeregisterForMessageEvent<PlayerTeamUpdateMessage>(OnPlayerTeamUpdateReceived);
            _messageBus.Dispose(); //Detaches the bus from the host's receive/disconnect events.

            _host.OnNewClientConnected -= OnNewClientConnected;
            _host.OnClientDisconnected -= OnClientDisconnected;

            //The lobby owns its listener 1:1 in every current flow, so disposing the lobby releases the
            //port and drops every connected client (their side surfaces it as a lost host - QF8).
            _host.Stop();
        }

        public bool TryLaunchGame(out string? failReason)
        {
            Debug.WriteLine("TryLaunchGame.");

            failReason = ValidateLaunchSettings();
            if (failReason != null)
                return false;

            _ = Launch();
            return true;
        }

        // #153 launch gate (decision 9): hard legality problems across every loaded army, for the UI's
        // "launch anyway?" confirm. The logic lives in the fixture-free ArmyBuilding.LaunchGate.
        public IReadOnlyList<string> ValidateArmiesForLaunch() =>
            ArmyBuilding.LaunchGate.ValidateArmies(
                _playerInfosFull.Values.Select(info => (info.PlayerName, info.ArmyListFile)), ArmyPoints);

        public bool TryResumeGame(out string? failReason)
        {
            if (!_isResume)
            {
                failReason = "This lobby was not created from a saved game.";
                return false;
            }

            failReason = null;
            _ = LaunchResume();
            return true;
        }

        // Re-crew a saved slot before resuming (Local / AI; networked client assignment is a follow-up).
        public void SetSavedSlotPlayerType(PlayerID slotPlayerID, EPlayerType playerType,
            FDG.Ai.EAiProfile aiProfile = FDG.Ai.EAiProfile.SoloRules)
        {
            if (_playerInfosFull.TryGetValue(slotPlayerID, out LobbyPlayerInfoFull? info))
            {
                info.PlayerType = playerType;
                info.AiProfile = aiProfile;
                UpdateInfoSummariesFromFullList();
            }
        }

        // Resume launch: the world already exists in the loaded store, so we only rebuild the
        // PlayerSlots (reusing saved PlayerIDs) and hand them to the resume FDGServer constructor,
        // which skips world creation. The saved PlayerSlotInfo entries are removed first so the
        // rebuilt slots don't create duplicates.
        private async Task LaunchResume()
        {
            _isLaunched = true; //Refuse late joiners from here on (QF6).
            await _messageBus.SendCommandToAllAsync(new LobbyChatMessage("System", LAUNCHING_GAME_MESSAGE));
            await Task.Delay(300);

            foreach (DataReference oldSlotInfo in _gameDataStore.GetAllDataReferences<PlayerSlotInfo>().ToList())
            {
                _gameDataStore.Destroy(oldSlotInfo);
            }

            FDGGame_AsLocal? gameModel = null;
            LobbyPlayerInfoFull[] infos = _playerInfosFull.Values.ToArray();
            PlayerSlot[] playerSlots = new PlayerSlot[infos.Length];

            for (int i = 0; i < playerSlots.Length; i++)
            {
                LobbyPlayerInfoFull info = infos[i];

                // ArmyListFile is vestigial on resume (armies are already in the loaded store), but the
                // PlayerSlot ctor requires one.
                PlayerSlot playerSlot = new PlayerSlot(i, (int)info.TeamNumber, info.PlayerID,
                    info.ArmyListFile ?? GetTempTestArmyFile(), _gameDataStore);
                playerSlots[i] = playerSlot;

                switch (info.PlayerType)
                {
                    case EPlayerType.Local:
                        gameModel ??= new FDGGame_AsLocal(_gameDataStore, _messageBus);
                        playerSlot.AssignPlayerController(new LocalPlayerController(info.PlayerName, playerSlot.PlayerID, gameModel));
                        gameModel.AddLocalPlayerID(playerSlot.PlayerID);
                        break;
                    case EPlayerType.Network:
                        playerSlot.AssignPlayerController(new NetworkPlayerController(info.PlayerName, playerSlot.PlayerID,
                            info.ConnectionID, _messageBus, _gameDataStore));
                        break;
                    case EPlayerType.AI:
                        FDGGame_AsLocal aiGame = new FDGGame_AsLocal(_gameDataStore, _messageBus);
                        playerSlot.AssignPlayerController(FDG.Ai.AiProfileFactory.CreateController(
                            info.AiProfile, info.PlayerName, playerSlot.PlayerID, aiGame,
                            _gameSettings.DiceSeed, playerSlot.SlotID));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // Resume on the GUI host animates just like a new game — give it a real-time clock so the
            // presentation beats play at a presentable tempo (without it the beats run instantly).
            // #265: the save owns the settings on resume, except the few a resume lobby may re-pick
            // (the table background) - see GameSettings.WithResumeOverridesFrom.
            FDGServer server = new FDGServer(_gameDataStore, _messageBus, playerSlots,
                new RealtimePresentationClock(), _gameSettings);
            server.OnGameEnded += HandleServerGameEnded;
            server.OnGameCompleted += HandleServerGameCompleted;

            if (gameModel != null)
            {
                OnLaunched?.Invoke(gameModel);
            }

            await _messageBus.SendCommandToAllAsync(new LaunchGameMessage());
        }

        // Fired by FDGServer when the game finishes (engine thread). Forward to this host's own front
        // end, and mirror the result to remote clients so they can return to the menu too — clients have
        // no FDGServer, so the wire message is their only clean game-end signal. Fire-and-forget: a send
        // failure (e.g. a client already gone) shouldn't disrupt the host's own end-of-game handling.
        private void HandleServerGameEnded(string result)
        {
            OnGameEnded?.Invoke(result);
            _ = _messageBus.SendCommandToAllAsync(new GameEndedMessage(result));
        }

        // The structured half of the same end-of-game signal, raised by FDGServer immediately before
        // OnGameEnded (engine thread). Forwarded synchronously and NOT mirrored over the wire (host-side
        // record; clients get the prose). #187's recovery save hangs off this: the front end serializes
        // the still-intact store here, before the game-over card starts any teardown.
        private void HandleServerGameCompleted(GameResult result)
        {
            OnGameCompleted?.Invoke(result);
        }

        private string? ValidateLaunchSettings()
        {
            switch (_gameSettings.TerrainPlacementMode)
            {
                case ETerrainPlacementMode.Alternating:
                    if (_gameSettings.TerrainPieceCount < 0 || _gameSettings.TerrainPieceCount > 30)
                        return $"Terrain piece count ({_gameSettings.TerrainPieceCount}) must be between 0 and 30.";
                    break;

                case ETerrainPlacementMode.AlternatingPoints:
                    if (_gameSettings.TerrainPointsTotal < 0 || _gameSettings.TerrainPointsTotal > Stages.PlaceTerrainStage.MaxPointsTotal)
                        return $"Terrain points total ({_gameSettings.TerrainPointsTotal}) must be between 0 and {Stages.PlaceTerrainStage.MaxPointsTotal}.";
                    if (_gameSettings.TerrainPointsPerTurn < 1 || _gameSettings.TerrainPointsPerTurn > Stages.PlaceTerrainStage.MaxPointsPerTurn)
                        return $"Terrain points per turn ({_gameSettings.TerrainPointsPerTurn}) must be between 1 and {Stages.PlaceTerrainStage.MaxPointsPerTurn}.";
                    break;

                case ETerrainPlacementMode.LoadFromFile:
                    if (string.IsNullOrWhiteSpace(_gameSettings.TerrainLayoutPath))
                        return "Terrain mode is 'Load From File' but no layout file was chosen.";
                    var layout = SaveLoad.TerrainLayoutLoader.TryLoadFromFile(_gameSettings.TerrainLayoutPath, out string? error);
                    if (layout == null)
                        return $"Could not load terrain layout: {error}";
                    break;
            }

            // #255: a game where everyone shares one team has no opposition - hard block (not the
            // overridable army-problems confirm). Solo lobbies are exempt so test launches keep working.
            if (_playerInfosFull.Count >= 2 &&
                _playerInfosFull.Values.Select(info => info.TeamNumber).Distinct().Count() == 1)
            {
                return "All players are on the same team - at least two teams must be represented.";
            }

            return null;
        }

        private async Task Launch()
        {
            _isLaunched = true; //Refuse late joiners from here on (QF6).
            LobbyChatMessage gameStartingMessage = new LobbyChatMessage("System", LAUNCHING_GAME_MESSAGE);
            //AddMessageToLocalList(gameStartingMessage);
            await _messageBus.SendCommandToAllAsync(gameStartingMessage);
            await Task.Delay(300);

            //Give a quick tribute. Easter egg — fires ~10% of launches.
            if (_tributeRng.Next(10) == 0)
            {
                LobbyChatMessage tributeMessage = new LobbyChatMessage("Mukumioke", "buck futter");
                //AddMessageToLocalList(tributeMessage);
                await _messageBus.SendCommandToAllAsync(tributeMessage);
                await Task.Delay(50);
            }

            FDGGame_AsLocal? gameModel = null; //We may not have a local player.

            //Make a player controller for each player.
            PlayerSlot[] playerSlots = new PlayerSlot[_playerInfosFull.Count];

            List<FDGGame_AsLocal> localPlayers = new List<FDGGame_AsLocal>(2);

            LobbyPlayerInfoFull[] lobbyPlayerInfosArray = _playerInfosFull.Values.ToArray();

            for (int i = 0; i < playerSlots.Length; i++)
            {
                LobbyPlayerInfoFull lobbyPlayerInfo = lobbyPlayerInfosArray[i];

                //TEMP this should not be okay.
                if(lobbyPlayerInfo.ArmyListFile == null)
                {
                    Console.WriteLine("Player had empty army list. Making a simple one.");
                    lobbyPlayerInfo.ArmyListFile = GetTempTestArmyFile();
                }

                PlayerSlot playerSlot = new PlayerSlot(i, (int)lobbyPlayerInfo.TeamNumber, lobbyPlayerInfo.PlayerID, 
                    lobbyPlayerInfo.ArmyListFile, _gameDataStore);
                playerSlots[i] = playerSlot;

                switch (lobbyPlayerInfo.PlayerType)
                {
                    case EPlayerType.Local:

                        if(gameModel == null)
                        {
                            gameModel = new FDGGame_AsLocal(_gameDataStore, _messageBus);
                        }

                        LocalPlayerController localPlayerController = new LocalPlayerController(lobbyPlayerInfo.PlayerName,
                            playerSlot.PlayerID, gameModel);
                        localPlayers.Add(gameModel);
                        playerSlot.AssignPlayerController(localPlayerController);

                        gameModel.AddLocalPlayerID(playerSlot.PlayerID);
                        break;
                    case EPlayerType.Network:
                        NetworkPlayerController networkPlayerController = new NetworkPlayerController(lobbyPlayerInfo.PlayerName, playerSlot.PlayerID,
                            lobbyPlayerInfo.ConnectionID, _messageBus, _gameDataStore);
                        playerSlot.AssignPlayerController(networkPlayerController);
                        break;
                    case EPlayerType.AI:
                        FDGGame_AsLocal aiGame = new FDGGame_AsLocal(_gameDataStore, _messageBus);
                        ComputerPlayerController aiController = FDG.Ai.AiProfileFactory.CreateController(
                            lobbyPlayerInfo.AiProfile, lobbyPlayerInfo.PlayerName, playerSlot.PlayerID, aiGame,
                            _gameSettings.DiceSeed, playerSlot.SlotID);
                        playerSlot.AssignPlayerController(aiController);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // The GUI host self-paces presentation in real time so the battle unfolds at a
            // presentable tempo; clients receive beats already spaced by this clock. (Headless play
            // goes through CliApp, which leaves FDGServer's default instant clock.)
            FDGServer server = new FDGServer(_gameDataStore, _messageBus, _gameSettings, playerSlots,
                new RealtimePresentationClock());
            server.OnGameEnded += HandleServerGameEnded;
            server.OnGameCompleted += HandleServerGameCompleted;

            if (gameModel != null) //Dedicated server really doesn't need to do this.
            {
                OnLaunched?.Invoke(gameModel);
            }

            LaunchGameMessage launchGameMessage = new LaunchGameMessage();
            await _messageBus.SendCommandToAllAsync(launchGameMessage);
        }
        
        private ArmyListFile GetTempTestArmyFile()
        {
            var armyfile  = new ArmyListFile()
            {
                Faction = "Test Faction",
                PointsLimit = 100,
                Name = "Test Army"
            };

            armyfile.Units.Add(GetTempTestUnit());


            return armyfile;
        }

        private UnitFileEntry GetTempTestUnit()
        {
            UnitFileEntry unitFileEntry = new UnitFileEntry();
            unitFileEntry.Name = "Test Unit";
            unitFileEntry.Quality = 4;
            unitFileEntry.Defense = 4;
            unitFileEntry.ModelCount = 5;
            unitFileEntry.PointCost = 100;

            WeaponFileEntry meleeWeaponFile = new WeaponFileEntry()
            {
                Name = "Stabby Knife",
                Attacks = 1,
                Quantity = 4,
                RangeInches = 0
            };

            WeaponFileEntry rangedWeaponFile = new WeaponFileEntry()
            {
                Name = "Shooty Gun",
                Attacks = 1,
                Quantity = 4,
                RangeInches = 18
            };

            unitFileEntry.Weapons.Add(meleeWeaponFile);
            unitFileEntry.Weapons.Add(rangedWeaponFile);

            WeaponFileEntry betterMeleeWeaponFile = new WeaponFileEntry()
            {
                Name = "Extra Stabby Knife",
                Attacks = 2,
                Quantity = 4,
                RangeInches = 0,
                ArmorPenetration = 4
            };

            WeaponFileEntry betterRangedWeaponFile = new WeaponFileEntry()
            {
                Name = "Extra Shooty Gun",
                Attacks = 2,
                Quantity = 1,
                RangeInches = 30,
                ArmorPenetration = 4
            };

            unitFileEntry.Weapons.Add(betterMeleeWeaponFile);
            unitFileEntry.Weapons.Add(betterRangedWeaponFile);

            return unitFileEntry;
        }

        public void SetArmyPoints(int armyPoints)
        {
            if (armyPoints > 0)
            {
                _settings_ArmyPoints.OnNext(armyPoints);
                _gameSettings.ArmyPoints = armyPoints;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));

            }
            else
            {
                _settings_ArmyPoints.OnNext(_settings_ArmyPoints.Value);
            }
        }

        public void SetTerrainCount(int terrainCount)
        {
            if (terrainCount > 0)
            {
                _settings_TerrainPieceCount.OnNext(terrainCount);
                _gameSettings.TerrainPieceCount = terrainCount;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
            }
            else
            {
                _settings_TerrainPieceCount.OnNext(_settings_TerrainPieceCount.Value);
            }
        }

        public void SetTerrainPointsTotal(int points)
        {
            // 0 is legal - it skips the terrain phase, the same as a One Per count of 0.
            if (points >= 0 && points <= Stages.PlaceTerrainStage.MaxPointsTotal)
            {
                _settings_TerrainPointsTotal.OnNext(points);
                _gameSettings.TerrainPointsTotal = points;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
            }
            else
            {
                _settings_TerrainPointsTotal.OnNext(_settings_TerrainPointsTotal.Value);
            }
        }

        public void SetTerrainPointsPerTurn(int points)
        {
            if (points >= 1 && points <= Stages.PlaceTerrainStage.MaxPointsPerTurn)
            {
                _settings_TerrainPointsPerTurn.OnNext(points);
                _gameSettings.TerrainPointsPerTurn = points;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
            }
            else
            {
                _settings_TerrainPointsPerTurn.OnNext(_settings_TerrainPointsPerTurn.Value);
            }
        }

        public void SetTerrainPlacementMode(ETerrainPlacementMode mode)
        {
            if (Enum.IsDefined(mode))
            {
                _settings_TerrainPlacementMode.OnNext(mode);
                _gameSettings.TerrainPlacementMode = mode;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
            }
            else
            {
                _settings_TerrainPlacementMode.OnNext(_settings_TerrainPlacementMode.Value);
            }
        }

        public void SetTerrainLayoutPath(string? path)
        {
            //Empty string and null are equivalent for "no file chosen".
            string? normalized = string.IsNullOrWhiteSpace(path) ? null : path;
            _settings_TerrainLayoutPath.OnNext(normalized);
            _gameSettings.TerrainLayoutPath = normalized;
            _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
        }

        public void SetObjectivePlacementMode(EObjectivePlacementMode mode)
        {
            if (Enum.IsDefined(mode))
            {
                _settings_ObjectivePlacementMode.OnNext(mode);
                _gameSettings.ObjectivePlacementMode = mode;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
            }
            else
            {
                _settings_ObjectivePlacementMode.OnNext(_settings_ObjectivePlacementMode.Value);
            }
        }

        public void SetRandomnessType(ERandomnessType randomnessType)
        {
            if (Enum.IsDefined(randomnessType))
            {
                _settings_RandomnessType.OnNext(randomnessType);
                _gameSettings.RandomnessType = randomnessType;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));

            }
            else
            {
                _settings_RandomnessType.OnNext(_settings_RandomnessType.Value);
            }
        }

        public void SetTurnStyle(ETurnStyle turnStyle)
        {
            if (Enum.IsDefined(turnStyle))
            {
                _settings_TurnMethod.OnNext(turnStyle);
                _gameSettings.TurnStyle = turnStyle;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));

            }
            else
            {
                _settings_TurnMethod.OnNext(_settings_TurnMethod.Value);
            }
        }

        public void SetCoverProximityExceptions(bool enabled)
        {
            _settings_CoverProximityExceptions.OnNext(enabled);
            _gameSettings.CoverProximityExceptions = enabled;
            _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
        }

        public void SetTableBackground(ETableBackground background)
        {
            if (Enum.IsDefined(background))
            {
                _settings_TableBackground.OnNext(background);
                _gameSettings.TableBackground = background;
                _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
            }
            else
            {
                _settings_TableBackground.OnNext(_settings_TableBackground.Value);
            }
        }

        public bool CheckCanModifyPlayerIDInfo(PlayerID playerID)
        {
            LobbyPlayerInfoSummary? queryPlayer = _playerInfos.Value.FirstOrDefault(info => info.PlayerID == playerID);

            if(queryPlayer == null)
            {
                Debug.WriteLine($"Queried about a player with an ID not found in the list: {playerID.ID}");
                return false;
            }

            return queryPlayer.PlayerType != EPlayerType.Network;
        }

        public void UpdateArmyListFile(PlayerID playerId, ArmyListFile armyListFile)
        {
            if(_playerInfosFull.ContainsKey(playerId) == false)
            {
                Debug.WriteLine($"Couldn't find ID of player {playerId}.");
                return;
            }

            _playerInfosFull[playerId].ArmyListFile = armyListFile;

            UpdateInfoSummariesFromFullList();
        }

        public void AddLocalPlayer()
        {
            string tempName = $"Local Player {(_playerInfos.Value.Count + 1)}"; //Will need to be able to set this later.
            Debug.WriteLine($"Added local player: {tempName}");

            //Assign a player ID to this person.
            //TODO: I may have decided to give players their IDs elsewhere but I can't remember, double check that.
            PlayerID newClientPlayerID = new PlayerID(Guid.NewGuid());

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(tempName, null, FirstEmptyTeam(),
                EPlayerType.Local, new ConnectionID(Guid.Empty), newClientPlayerID);
            _playerInfosFull.Add(newClientPlayerID, newLobbyPlayerInfo);
            UpdateInfoSummariesFromFullList();

            LobbyGameSettingsUpdate gameSettingsUpdate = new LobbyGameSettingsUpdate(_gameSettings);
            _messageBus.SendCommandToAllAsync(gameSettingsUpdate);
        }

        public void AddAiPlayer(FDG.Ai.EAiProfile profile)
        {
            // #191 A6: the profile is the product name in the lobby - "Tactician Bot" is the
            // challenge AI, "DerpBot" the legacy solo-rules bot (Chris's naming).
            string botName = profile == FDG.Ai.EAiProfile.Tactician ? "Tactician Bot" : "DerpBot";
            // #217: number by rank among same-profile bots (first Tactician Bot is "Tactician Bot 1"
            // regardless of humans or other bot types), not by total player count.
            int botNumber = _playerInfosFull.Values.Count(info =>
                info.PlayerType == EPlayerType.AI && info.AiProfile == profile) + 1;
            string name = $"{botName} {botNumber}";
            Debug.WriteLine($"Added AI player: {name}");

            PlayerID newPlayerID = new PlayerID(Guid.NewGuid());

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(name, GetTempTestArmyFile(),
                FirstEmptyTeam(), EPlayerType.AI, new ConnectionID(Guid.Empty), newPlayerID,
                profile);
            _playerInfosFull.Add(newPlayerID, newLobbyPlayerInfo);
            UpdateInfoSummariesFromFullList();

            LobbyGameSettingsUpdate gameSettingsUpdate = new LobbyGameSettingsUpdate(_gameSettings);
            _messageBus.SendCommandToAllAsync(gameSettingsUpdate);

            _ = SendBotGreetingAsync(name); //Fire-and-forget; the bot speaks a few seconds from now.
        }

        // One line per bot, a few seconds after it joins. Broadcast rather than AddMessageToLocalList so
        // networked players see it too, and carried by the same LobbyChatMessage the humans use - that
        // type is only a sender name plus text, so a bot's line is indistinguishable from a person's.
        private async Task SendBotGreetingAsync(string botName)
        {
            await Task.Delay(Random.Shared.Next(BotGreetingMinDelayMs, BotGreetingMaxDelayMs + 1));

            //The lobby can be torn down or launched while a bot is still winding up; either way the
            //chat log it would post into is already gone.
            if (_isDisposed || _isLaunched)
            {
                return;
            }

            await _messageBus.SendCommandToAllAsync(new LobbyChatMessage(botName, TakeUnusedGreeting()));
        }

        // Picks a line no bot has used yet. A lobby that somehow exhausts the pool recycles rather than
        // falling silent - with 8 players max a real lobby never gets there, so in practice this just
        // guarantees every bot present says something different.
        private string TakeUnusedGreeting()
        {
            lock (_botGreetingLock)
            {
                if (_usedBotGreetings.Count >= BOT_GREETINGS.Length)
                {
                    _usedBotGreetings.Clear();
                }

                List<int> unused = new List<int>(BOT_GREETINGS.Length);
                for (int i = 0; i < BOT_GREETINGS.Length; i++)
                {
                    if (!_usedBotGreetings.Contains(i))
                    {
                        unused.Add(i);
                    }
                }

                int chosen = unused[Random.Shared.Next(unused.Count)];
                _usedBotGreetings.Add(chosen);
                return BOT_GREETINGS[chosen];
            }
        }
    }
}
