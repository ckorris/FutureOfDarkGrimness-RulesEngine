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
        public IObservable<ETerrainPlacementMode> TerrainPlacementModeObservable => _settings_TerrainPlacementMode;
        public IObservable<string?> TerrainLayoutPathObservable => _settings_TerrainLayoutPath;
        public IObservable<ERandomnessType> RandomnessTypeObservable => _settings_RandomnessType;
        public IObservable<ETurnStyle> TurnStyleObservable => _settings_TurnMethod;

        public string ServerName => _serverName.Value;

        public IReadOnlyList<LobbyChatMessage> ChatMessages => _chatMessages;

        public IReadOnlyList<LobbyPlayerInfoSummary> PlayerInfos => _playerInfos.Value;

        public int ArmyPoints => _settings_ArmyPoints.Value;

        public int TerrainCount => _settings_TerrainPieceCount.Value;

        public ETerrainPlacementMode TerrainPlacementMode => _settings_TerrainPlacementMode.Value;

        public string? TerrainLayoutPath => _settings_TerrainLayoutPath.Value;

        public ERandomnessType RandomnessType => _settings_RandomnessType.Value;

        public ETurnStyle TurnStyle => _settings_TurnMethod.Value;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessagesSubject;
        private readonly List<LobbyChatMessage> _chatMessages = new List<LobbyChatMessage>();

        private BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>> _playerInfos;
        private Dictionary<PlayerID, LobbyPlayerInfoFull> _playerInfosFull = new Dictionary<PlayerID, LobbyPlayerInfoFull>();

        private BehaviorSubject<int> _settings_ArmyPoints;
        private BehaviorSubject<int> _settings_TerrainPieceCount;
        private BehaviorSubject<ETerrainPlacementMode> _settings_TerrainPlacementMode;
        private BehaviorSubject<string?> _settings_TerrainLayoutPath;
        private BehaviorSubject<ERandomnessType> _settings_RandomnessType;
        private BehaviorSubject<ETurnStyle> _settings_TurnMethod;

        private INetworkHost _host;

        private string _hostPlayerName;

        private MessageBusHost_Networked _messageBus;

        private const string SERVER_START_MESSAGE = "Server started successfully.";
        private const string LAUNCHING_GAME_MESSAGE = "About to launch game.";

        private static readonly Random _tributeRng = new Random();

        public event Action<IFDGGame>? OnLaunched;

        private GameSettings _gameSettings = GameSettings.GetDefault();

        IReadWriteableGameDataStore _gameDataStore;

        private readonly bool _isResume;

        public bool IsResumeMode => _isResume;

        public LobbyViewModel_Host(string hostPlayerName, string serverName, string? password, INetworkHost host)
        {
            _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            _messageBus = new MessageBusHost_Networked(host, _gameDataStore);
            _host = host;
            _hostPlayerName = hostPlayerName;
            PlayerID thisPlayerID = new PlayerID(Guid.NewGuid());

            _serverName = new BehaviorSubject<string>(serverName);
            _chatMessagesSubject = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(_gameSettings.ArmyPoints);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(_gameSettings.TerrainPieceCount);
            _settings_TerrainPlacementMode = new BehaviorSubject<ETerrainPlacementMode>(_gameSettings.TerrainPlacementMode);
            _settings_TerrainLayoutPath = new BehaviorSubject<string?>(_gameSettings.TerrainLayoutPath);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(_gameSettings.RandomnessType);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(_gameSettings.TurnStyle);

            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>>(new List<LobbyPlayerInfoSummary>());

            host.OnNewClientConnected += OnNewClientConnected;
            host.OnClientDisconnected += OnClientDisconnected;

            _messageBus.RegisterForMessageEvent<LobbyChatMessage>(OnLocalChatMessageReceived);
            _messageBus.RegisterForMessageEvent<LobbyChatMessage_FromClient>(OnChatMessageReceived);
            _messageBus.RegisterForMessageEvent<NewLobbyClientGreeting>(OnReceiveNewClientGreeting);
            _messageBus.RegisterForMessageEvent<ArmyListUpdateMessage>(OnArmyListFileUpdateReceived);

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
            _hostPlayerName = hostPlayerName;

            // Faithfully resume the saved game's settings (turn style, randomness, etc.).
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(loadedGameDataStore);
            _gameSettings = progress?.Settings ?? GameSettings.GetDefault();

            _serverName = new BehaviorSubject<string>(serverName);
            _chatMessagesSubject = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(_gameSettings.ArmyPoints);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(_gameSettings.TerrainPieceCount);
            _settings_TerrainPlacementMode = new BehaviorSubject<ETerrainPlacementMode>(_gameSettings.TerrainPlacementMode);
            _settings_TerrainLayoutPath = new BehaviorSubject<string?>(_gameSettings.TerrainLayoutPath);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(_gameSettings.RandomnessType);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(_gameSettings.TurnStyle);

            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>>(new List<LobbyPlayerInfoSummary>());

            host.OnNewClientConnected += OnNewClientConnected;
            host.OnClientDisconnected += OnClientDisconnected;

            _messageBus.RegisterForMessageEvent<LobbyChatMessage>(OnLocalChatMessageReceived);
            _messageBus.RegisterForMessageEvent<LobbyChatMessage_FromClient>(OnChatMessageReceived);
            _messageBus.RegisterForMessageEvent<NewLobbyClientGreeting>(OnReceiveNewClientGreeting);
            _messageBus.RegisterForMessageEvent<ArmyListUpdateMessage>(OnArmyListFileUpdateReceived);

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
            //Test: Just send it the current player list.

            //Maybe do nothing?
        }

        private void OnReceiveNewClientGreeting(NewLobbyClientGreeting greeting)
        {
            // Resume games have a fixed saved roster — route joining clients to saved slots instead of
            // creating new ones. Strictly isolated so the new-game path below is unchanged.
            if (_isResume)
            {
                OnResumeClientGreeting(greeting);
                return;
            }

            Debug.WriteLine($"Received greeting from new client: {greeting.PlayerName}");

            //Assign a player ID to this person. 
            //TODO: I may have decided to give players their IDs elsewhere but I can't remember, double check that.
            PlayerID newClientPlayerID = new PlayerID(Guid.NewGuid());
            _messageBus.SendCommandToAllAsync(new LobbyPlayerIDAssignment(newClientPlayerID));

            //Send the server name.
            LobbyServerNameMessage lobbyServerNameMessage = new LobbyServerNameMessage(_serverName.Value);
            _messageBus.SendCommandToAllAsync(lobbyServerNameMessage);

            //TODO: Have something behind the player info list instead of doing this.
            int tempTeamNumber = _playerInfos.Value.Count + 1;

            //ArmyListSummary tempSummary = new ArmyListSummary("Knifeybois", "Alien Hives", 2000);

            ConnectionID connectionID = _messageBus.GetCurrentMessageConnectionID();

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(greeting.PlayerName, null, (ETeamOption)tempTeamNumber,
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
        private void OnResumeClientGreeting(NewLobbyClientGreeting greeting)
        {
            Debug.WriteLine($"[#052] Resume: greeting from {greeting.PlayerName}.");

            ConnectionID connectionID = _messageBus.GetCurrentMessageConnectionID();

            LobbyPlayerInfoFull? openSlot = _playerInfosFull.Values.FirstOrDefault(p => p.PlayerType == EPlayerType.AI);
            if (openSlot == null)
            {
                Debug.WriteLine("[#052] Resume: no open (AI) saved slot for the joining client.");
                return;
            }

            openSlot.PlayerType = EPlayerType.Network;
            openSlot.PlayerName = greeting.PlayerName;
            openSlot.ConnectionID = connectionID;

            // Adopt the saved slot's PlayerID (not a fresh one) so the client resumes as that player.
            _messageBus.SendCommandToAllAsync(new LobbyPlayerIDAssignment(openSlot.PlayerID));
            _messageBus.SendCommandToAllAsync(new LobbyServerNameMessage(_serverName.Value));
            _messageBus.SendCommandToAllAsync(new LobbyGameSettingsUpdate(_gameSettings));
            UpdateInfoSummariesFromFullList();
        }

        private void OnClientDisconnected(ConnectionID disconnectedConnectionID)
        {
            PlayerID leavingPlayerID = _playerInfosFull.First(info => info.Value.ConnectionID == disconnectedConnectionID).Key;
            _playerInfosFull.Remove(leavingPlayerID);
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
                    fullInfo.ConnectionID, fullInfo.PlayerID));
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

        public void Dispose()
        {
            _messageBus.DeregisterForMessageEvent<LobbyChatMessage_FromClient>(OnChatMessageReceived);
            _host.OnNewClientConnected -= OnNewClientConnected;
            _host.OnClientDisconnected -= OnClientDisconnected;
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
        public void SetSavedSlotPlayerType(PlayerID slotPlayerID, EPlayerType playerType)
        {
            if (_playerInfosFull.TryGetValue(slotPlayerID, out LobbyPlayerInfoFull? info))
            {
                info.PlayerType = playerType;
                UpdateInfoSummariesFromFullList();
            }
        }

        // Resume launch: the world already exists in the loaded store, so we only rebuild the
        // PlayerSlots (reusing saved PlayerIDs) and hand them to the resume FDGServer constructor,
        // which skips world creation. The saved PlayerSlotInfo entries are removed first so the
        // rebuilt slots don't create duplicates.
        private async Task LaunchResume()
        {
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
                        playerSlot.AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                            info.PlayerName, playerSlot.PlayerID, aiGame));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // Resume on the GUI host animates just like a new game — give it a real-time clock so the
            // presentation beats play at a presentable tempo (without it the beats run instantly).
            FDGServer server = new FDGServer(_gameDataStore, _messageBus, playerSlots,
                new RealtimePresentationClock());

            if (gameModel != null)
            {
                OnLaunched?.Invoke(gameModel);
            }

            await _messageBus.SendCommandToAllAsync(new LaunchGameMessage());
        }

        private string? ValidateLaunchSettings()
        {
            switch (_gameSettings.TerrainPlacementMode)
            {
                case ETerrainPlacementMode.Alternating:
                    if (_gameSettings.TerrainPieceCount < 0 || _gameSettings.TerrainPieceCount > 30)
                        return $"Terrain piece count ({_gameSettings.TerrainPieceCount}) must be between 0 and 30.";
                    break;

                case ETerrainPlacementMode.LoadFromFile:
                    if (string.IsNullOrWhiteSpace(_gameSettings.TerrainLayoutPath))
                        return "Terrain mode is 'Load From File' but no layout file was chosen.";
                    var layout = SaveLoad.TerrainLayoutLoader.TryLoadFromFile(_gameSettings.TerrainLayoutPath, out string? error);
                    if (layout == null)
                        return $"Could not load terrain layout: {error}";
                    break;
            }
            return null;
        }

        private async Task Launch()
        {
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
                        ComputerPlayerController aiController = AiResolverRegistryFactory.CreateSoloRulesController(
                            lobbyPlayerInfo.PlayerName, playerSlot.PlayerID, aiGame);
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

        /*
        private PlayerSlot[] GetPlayerSlots(IStageResolverRegistry stageResolverRegister, IReadableGameDataStore gameDataStore)
        {
            PlayerSlot[] playerSlots = new PlayerSlot[_playerInfos.Value.Count];

            //Local players will need to have registries assigned locally, so cache them specifically for this.
            List<LocalPlayerController> localPlayerControllers = new List<LocalPlayerController>();

            for (int i = 0; i < playerSlots.Length; i++)
            {
                LobbyPlayerInfoSummary lobbyPlayerInfo = _playerInfos.Value[i];

                PlayerSlot playerSlot = new PlayerSlot(i, (int)lobbyPlayerInfo.TeamNumber);
                playerSlots[i] = playerSlot;

                switch (lobbyPlayerInfo.PlayerType)
                {
                    case EPlayerType.Local:
                        LocalPlayerController localPlayerController = new LocalPlayerController(lobbyPlayerInfo.PlayerName, 
                            playerSlot.PlayerID, stageResolverRegister);
                        localPlayerControllers.Add(localPlayerController);
                        playerSlot.AssignPlayerController(localPlayerController);
                        break;
                    case EPlayerType.Network:
                        NetworkPlayerController networkPlayerController = new NetworkPlayerController(lobbyPlayerInfo.PlayerName, playerSlot.PlayerID,
                            lobbyPlayerInfo.connectionID, _commandDispatcher, gameDataStore);
                            break;
                    case EPlayerType.AI:
                        throw new NotImplementedException();
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return playerSlots;
        }
        */

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

            //TODO: Have something behind the player info list instead of doing this.
            int tempTeamNumber = _playerInfos.Value.Count + 1;

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(tempName, null, (ETeamOption)tempTeamNumber,
                EPlayerType.Local, new ConnectionID(Guid.Empty), newClientPlayerID);
            _playerInfosFull.Add(newClientPlayerID, newLobbyPlayerInfo);
            UpdateInfoSummariesFromFullList();

            LobbyGameSettingsUpdate gameSettingsUpdate = new LobbyGameSettingsUpdate(_gameSettings);
            _messageBus.SendCommandToAllAsync(gameSettingsUpdate);
        }

        public void AddAiPlayer()
        {
            int playerNumber = _playerInfos.Value.Count + 1;
            string name = $"Computer Player {playerNumber}";
            Debug.WriteLine($"Added AI player: {name}");

            PlayerID newPlayerID = new PlayerID(Guid.NewGuid());
            int tempTeamNumber = playerNumber;

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(name, GetTempTestArmyFile(),
                (ETeamOption)tempTeamNumber, EPlayerType.AI, new ConnectionID(Guid.Empty), newPlayerID);
            _playerInfosFull.Add(newPlayerID, newLobbyPlayerInfo);
            UpdateInfoSummariesFromFullList();

            LobbyGameSettingsUpdate gameSettingsUpdate = new LobbyGameSettingsUpdate(_gameSettings);
            _messageBus.SendCommandToAllAsync(gameSettingsUpdate);
        }
    }
}
