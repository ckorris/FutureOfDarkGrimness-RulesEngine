using FDG.Data;
using FDG.MessageBus;
using FDG.SaveLoad;
using FDG.Network.Connection;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.Presentation;
using FDG.Replay;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Previews;
using FDG.Stages;
using FDG.StateMachine.StateMachineBuilders;
using FDG.TextInterface;
using FDG.Utilities;
using FDG.StateMachine.StateMachineBuilders;
using System.Diagnostics;

namespace FDG.GameModel
{
    public class FDGServer
    {
        /// <summary>
        /// The end-of-game sentence ("X wins!", "It's a tie!", a disconnect/fault reason). Consumed by the
        /// front ends and forwarded over the wire; always fires alongside <see cref="OnGameCompleted"/>.
        /// </summary>
        public event Action<string>? OnGameEnded;

        /// <summary>
        /// The structured end-of-game record (#192), for automated play: benchmarks, self-play training,
        /// and search rollouts read the winner and score margin from here instead of parsing
        /// <see cref="OnGameEnded"/>'s prose. Host-side only; never crosses the wire. Fires exactly once,
        /// immediately before <see cref="OnGameEnded"/>.
        /// </summary>
        public event Action<GameResult>? OnGameCompleted;

        private IReadWriteableGameDataStore _gameDataStore;
        private IMessageBusHost _messageBusHost;
        private GameDataUpdateSender _synchronizer;

        // #190: re-broadcasts a unit's/model's owning entry whenever its in-place TokenContainer mutates, so
        // mid-game token changes reach remote clients (they otherwise only saw the join snapshot's tokens).
        // Held only for ownership/lifetime; nothing else reads it (it lives on its store subscriptions).
        private TokenChangeBroadcaster _tokenChangeBroadcaster;
        private PlayerSlotManager _playerSlotManager;
        private GameContext _gameContext;
        private StateMachine<IGameContext> _stateMachine;

        // #280: validates + re-broadcasts remote clients' live decision previews. Held only for
        // ownership/lifetime clarity - nothing else reads it (it lives on its bus registrations).
        private PreviewRelayer _previewRelayer;

        // The presentation clock the GUI host injects (null → instant, for headless/automated/resume).
        // Stored so the extracted BuildContextAndLaunch can reach it on both new-game and resume paths.
        private IPresentationClock? _presentationClock;

        // The shared army-load rule registry (core catalog + every army's embedded rules), built by
        // BuildRuleResolver on BOTH the new-game and resume paths. Threaded into GameContext →
        // RuleEvaluator so token-granted rules (auras / "gains rule X" buffs) resolve their names back to
        // definitions — including grants restored from a save (#101). Nullable only for the brief window
        // before either constructor builds it.
        private IRuleResolver? _ruleResolver;

        // #191 B1 5c: null for every real game; set only on the simulation path (see the resume
        // constructor's `simulation` parameter).
        private FDG.Simulation.SimulationHostOptions? _simulation;

        // #450: the game's replay recorder, when the launch path asked for one (null for simulations,
        // FdgLab and tests), and the marker sink the stages report boundaries through.
        private ReplayRecorder? _replay;
        private ReplayMarkerRelay? _replayMarkers;

        // #450: the game's persistent identity (GameProgressData.GameId) - minted for a new game, read back
        // (or minted, for a pre-#450 save or a compiled scenario) on resume.
        private Guid _gameId;

        /// <summary>#450: this game's persistent identity (see <see cref="GameProgressData.GameId"/>).</summary>
        public Guid GameId => _gameId;

        private static bool TEST_SINGLE_TURN = false; //Turn on to skip most of the game and just do one run of a model's activation.

        /// <param name="replay">#450: records the game for a replay. Real-game launch paths pass one;
        /// simulations, FdgLab and tests pass nothing and nothing is recorded.</param>
        public FDGServer(IReadWriteableGameDataStore gameDataStore, IMessageBusHost messageBusHost,
            GameSettings gameSettings, PlayerSlot[] playerSlots, IPresentationClock? presentationClock = null,
            ReplayRecorder? replay = null)
        {
            Debug.WriteLine($"Started {nameof(FDGServer)} (new game).");

            _replay = replay;
            _gameId = Guid.NewGuid();
            // #450 D5: every game runs under a concrete seed, so its replay can always say which one. Done
            // before anything reads the settings, so the seed the dice use is the one the progress record
            // (and so every save) carries.
            gameSettings = WithConcreteSeed(gameSettings);

            _gameDataStore = gameDataStore;
            _messageBusHost = messageBusHost;
            _presentationClock = presentationClock;
            _synchronizer = new GameDataUpdateSender(gameDataStore, messageBusHost);

            // #190: constructed BEFORE CreateArmies so it hooks every unit/model as it is created.
            _tokenChangeBroadcaster = new TokenChangeBroadcaster(gameDataStore);

            //For players/player slots, work backwards from here to create what you need to send updates to players,
            //then what you need to make that thing, then what you need for that, etc. until you lead back to these args.

            _playerSlotManager = new PlayerSlotManager(playerSlots);

            GameBootstrap.AddTeams(playerSlots, gameDataStore);

            CreateArmies(playerSlots, gameDataStore);

            BuildContextAndLaunch(gameSettings, applyCreationRules: true, resumeProgress: null);
        }

        /// <summary>
        /// Resumes a loaded game (work item #052). The store is already populated (world +
        /// <see cref="GameProgressData"/>) by <see cref="FDG.SaveLoad.GameSaveSerializer"/>, so this
        /// does NOT recreate teams/armies/models or re-apply creation rules; it reads settings + flow
        /// state from the save and resumes the state machine in the main phase. The player slots map
        /// (re-crewed) players to the saved <see cref="PlayerID"/>s.
        /// </summary>
        /// <param name="lobbySettings">The resume lobby's settings, when a lobby launched this. Only the
        /// fields <see cref="GameSettings.WithResumeOverridesFrom"/> names are taken from it - the save
        /// stays authoritative for everything that is already spent or would change the rules mid-game.
        /// Omitted (headless resume, the --scenario launch) means the save's settings verbatim.</param>
        /// <param name="simulation">
        /// #191 B1 5c: set ONLY by <see cref="FDG.Simulation.SimulationService"/>. Supplies the
        /// activation-boundary pause hook and replaces the bus-and-JSON request path with a direct
        /// one. Null for every real game, which is what keeps this constructor's behavior identical
        /// for the GUI, headless, networked and benchmark paths.
        /// </param>
        public FDGServer(IReadWriteableGameDataStore loadedGameDataStore, IMessageBusHost messageBusHost,
            PlayerSlot[] playerSlots, IPresentationClock? presentationClock = null,
            GameSettings? lobbySettings = null, FDG.Simulation.SimulationHostOptions? simulation = null,
            ReplayRecorder? replay = null)
        {
            _simulation = simulation;
            _replay = replay;
            Debug.WriteLine($"Started {nameof(FDGServer)} (resume).");

            _gameDataStore = loadedGameDataStore;
            _messageBusHost = messageBusHost;
            _presentationClock = presentationClock;
            _synchronizer = new GameDataUpdateSender(loadedGameDataStore, messageBusHost);

            // #190: the store is already populated on resume, so this hooks every loaded unit/model via its
            // enumerate-existing pass (OnCreated already fired before this helper existed).
            _tokenChangeBroadcaster = new TokenChangeBroadcaster(loadedGameDataStore);

            _playerSlotManager = new PlayerSlotManager(playerSlots);

            GameProgressData? progress = GameProgressUtilities.TryGetProgress(loadedGameDataStore);
            if (progress == null)
            {
                throw new InvalidOperationException(
                    "Tried to resume a game from a store that has no GameProgressData.");
            }

            // #101: rebuild the shared rule resolver on resume too. Granted-rule tokens (auras / buffs)
            // round-trip in the save, but the evaluator can't read them back to their definitions without a
            // resolver — and CreateArmies, which builds it for a new game, doesn't run on the resume path.
            // No re-grant is needed: the tokens themselves survive (re-applying creation rules would both
            // double the grants and reset Tough wounds, which is why the resume path skips that pass).
            var __gSrvResolver = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            RuleResolver ruleResolver = BuildRuleResolver(playerSlots);
            global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.SrvResolver, __gSrvResolver);

            // #095: the slot army files are vestigial here, so that resolver holds core rules only. Top it
            // up from each army's persisted definitions - otherwise a grant naming an embedded rule finds
            // nothing - and re-resolve the spell lists, which are [JsonIgnore] on ArmyData.
            var __gSrvRestore = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            GameBootstrap.RestoreArmyRuleData(ruleResolver, loadedGameDataStore);
            global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.SrvRestore, __gSrvRestore);

            // #265: fold in the resume lobby's re-pickable settings, and write them straight back onto the
            // progress record. The rolling save point rewrites that record from GameContext.Settings at
            // every activation boundary anyway, so this only closes the window before the first one - a
            // save taken immediately after resuming would otherwise still carry the old value.
            GameSettings settings = lobbySettings.HasValue
                ? progress.Settings.WithResumeOverridesFrom(lobbySettings.Value)
                : progress.Settings;
            // #450 D5: a save from before every game was seeded resumes under a concrete seed too; written
            // back with the rest, so the next save carries it.
            settings = WithConcreteSeed(settings);
            progress.Settings = settings;
            // #450: keep the game's identity across the resume; a pre-#450 save or a freshly compiled
            // scenario has none yet, so this resume is where it is minted. Never for a simulation: it is a
            // throwaway copy of a game, not a new one, and its runs must stay byte-identical (#191).
            _gameId = progress.GameId != Guid.Empty || simulation != null ? progress.GameId : Guid.NewGuid();
            progress.GameId = _gameId;
            GameProgressUtilities.WriteProgress(loadedGameDataStore, progress);

            var __gSrvLaunch = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            BuildContextAndLaunch(settings, applyCreationRules: false, resumeProgress: progress);
            global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.SrvLaunch, __gSrvLaunch);
        }

        private void BuildContextAndLaunch(GameSettings gameSettings, bool applyCreationRules, GameProgressData? resumeProgress)
        {
            LogAndChatMessageRelayer chatMessageRelayer = new LogAndChatMessageRelayer(_playerSlotManager);

            // The players' log. #450: when recording, the stages write through a decorator that also records
            // each line; the recorder's own one failure line goes to the undecorated sink (no recursion).
            ITextOutput playerOutput = new PlayerLogSender(chatMessageRelayer);
            ITextOutput textOutput = _replay != null ? new ReplayTextOutput(playerOutput, _replay) : playerOutput;

            TableState tableState = new TableState(_gameDataStore);

            // The host owns presentation pacing. Default to instant (headless / automated /
            // tests stay deterministic); the GUI host injects a real-time clock so the battle
            // unfolds at a presentable tempo. State computation is never paced — only emission.
            IPresentationClock presentationClock_ = _presentationClock ?? new InstantPresentationClock();
            PresentationRelayer presentationRelayer = new PresentationRelayer(_playerSlotManager, presentationClock_)
            {
                Observer = _replay,
            };

            // #191 B1 5c: a simulation answers decisions straight from the target slot's registry
            // (DirectPlayerRequester) instead of serializing them onto the bus. RequestMessageSender
            // is still constructed on that path only if it is the one being used - a simulation has
            // no connections to listen to, so it is skipped entirely.
            IPlayerRequestByID playerRequester;
            if (_simulation?.PlayerRequester != null)
            {
                playerRequester = _simulation.PlayerRequester;
            }
            else
            {
                playerRequester = new RequestMessageSender(_messageBusHost, _gameDataStore,
                    _playerSlotManager, textOutput)
                {
                    Observer = _replay,
                };
            }

            // #280: relay remote clients' live decision previews (ghosts / planned paths) to every
            // player. Runs on both the new-game and resume paths, GUI and headless hosts alike.
            _previewRelayer = new PreviewRelayer(_messageBusHost, _playerSlotManager, textOutput);

            if (_replay != null)
            {
                _replayMarkers = new ReplayMarkerRelay(_replay, _messageBusHost, _gameDataStore as GameDataStore, playerOutput);
            }

            _gameContext = new GameContext(textOutput, GetDiceRoller(gameSettings), playerRequester,
                tableState, _gameDataStore, presentationRelayer, gameSettings, resumeProgress, _ruleResolver,
                _simulation?.BoundaryHook, _gameId, _replayMarkers);
            _gameContext.OnGameCompleted += RaiseGameCompleted;

            // #042 creation-time rules (Tough): set each model's max wounds now that the evaluator
            // exists. Skipped on resume — max wounds are already in the loaded store (persisted on
            // ModelData) and re-applying would reset saved damage.
            if (applyCreationRules)
            {
                foreach (DataBinding<UnitData> unitBinding in _gameContext.GameDataStore.GetAllDataBindings<UnitData>())
                {
                    UnitCreationRules.Apply(unitBinding.GetValue(), _gameContext.RuleEvaluator);
                }
            }

            // #450: the recording starts here - after world creation and creation rules, before the state
            // machine runs - so snapshot 0 holds the finished world and map setup / deployment are deltas on it.
            StartReplay(gameSettings, playerOutput, resumeProgress);

            if (TEST_SINGLE_TURN)
            {
                LaunchSingleTurnTester(_gameContext);
                return;
            }

            _stateMachine = new StateMachine<IGameContext>(new GDFStateMachineBuilder(), _gameContext);

            _ = LaunchStateMachineOnceReady(_stateMachine, _gameContext, resumeProgress != null);
        }



        private void CreateArmies(PlayerSlot[] playerSlots, IReadWriteableGameDataStore gameDataStore)
        {
            RuleResolver ruleResolver = BuildRuleResolver(playerSlots);

            for (int i = 0; i < playerSlots.Length; i++)
            {
                GameBootstrap.CreateArmy(playerSlots[i].PlayerID, playerSlots[i].ArmyListFile, gameDataStore, ruleResolver);
            }
        }

        /// <summary>
        /// Builds the shared in-game rule resolver via <see cref="GameBootstrap.BuildRuleResolver"/> and
        /// holds it on <see cref="_ruleResolver"/> for <see cref="BuildContextAndLaunch"/> to hand to
        /// the <see cref="RuleEvaluator"/>. The evaluator needs it to read granted-rule tokens back to
        /// their definitions (auras / "gains rule X" buffs). Called by BOTH paths: on resume the surviving
        /// RuleGrant tokens are inert without a resolver, and <see cref="CreateArmies"/> (which built it for
        /// a new game) doesn't run. On resume the army files are typically vestigial (armies already live in
        /// the loaded store), so this pass registers little beyond the core catalog there —
        /// <see cref="GameBootstrap.RestoreArmyRuleData"/> supplies the embedded definitions from the save.
        /// </summary>
        private RuleResolver BuildRuleResolver(PlayerSlot[] playerSlots)
        {
            RuleResolver ruleResolver = GameBootstrap.BuildRuleResolver(playerSlots);
            _ruleResolver = ruleResolver; // #101: shared in-game for granted-rule projection (auras / buffs)
            return ruleResolver;
        }

        /// <summary>#450 D5: <paramref name="settings"/> with a seed picked for it when it has none.</summary>
        internal static GameSettings WithConcreteSeed(GameSettings settings)
        {
            settings.DiceSeed ??= Random.Shared.Next();
            return settings;
        }

        /// <summary>
        /// #450: starts the recorder (header, store subscription) and records GameStart + snapshot 0. Any
        /// failure is reported once and leaves the game running unrecorded - the replay must never cost the
        /// game.
        /// </summary>
        private void StartReplay(GameSettings settings, ITextOutput playerOutput, GameProgressData? resumeProgress)
        {
            if (_replay == null)
            {
                return;
            }

            try
            {
                _replay.Start(BuildReplayHeader(settings), playerOutput);
                _replay.AttachStore(_gameDataStore);
                _replayMarkers?.GameStart(resumeProgress?.RoundCount);
            }
            catch (Exception exception)
            {
                _replay.Dispose();
                playerOutput.Log($"[replay] Recording could not start ({exception.GetType().Name}). This game will have no replay.");
            }
        }

        private ReplayHeader BuildReplayHeader(GameSettings settings)
        {
            List<ArmyData> armies = _gameDataStore.GetAllValues<ArmyData>().ToList();
            List<ReplaySlot> slots = new List<ReplaySlot>();
            PlayerID? recordedBy = null;

            foreach (PlayerSlot slot in _playerSlotManager.PlayerSlots)
            {
                EReplayController controller = slot.Controller switch
                {
                    LocalPlayerController => EReplayController.Human,
                    NetworkPlayerController => EReplayController.Remote,
                    _ => EReplayController.Ai,
                };

                if (controller == EReplayController.Human && recordedBy == null)
                {
                    recordedBy = slot.PlayerID;
                }

                slots.Add(new ReplaySlot
                {
                    SlotId = slot.SlotID,
                    PlayerId = slot.PlayerID,
                    Name = slot.Name,
                    // The store's army, not the slot's file: on resume the file is vestigial.
                    ArmyName = armies.FirstOrDefault(army => army.IsOwnedBy(slot.PlayerID))?.ArmyName
                        ?? slot.ArmyListFile?.Name ?? "",
                    Team = slot.TeamNumber,
                    Controller = controller,
                });
            }

            return new ReplayHeader
            {
                BuildVersion = ReplayFormat.BuildVersion,
                EngineVersion = ReplayFormat.EngineVersion,
                GameId = _gameId,
                Seed = settings.DiceSeed,
                RandomnessType = settings.RandomnessType,
                Settings = settings,
                Slots = slots,
                RecordedBy = EReplayRecordedBy.Host,
                RecordedByPlayerId = recordedBy,
                StartedUtc = DateTime.UtcNow,
            };
        }

        private IDiceRoller GetDiceRoller(GameSettings gameSettings)
        {
            IDiceRoller diceRoller = gameSettings.RandomnessType switch
            {
                // #193: probabilistic combat is pure expected-value math, but its decisive rolls (morale,
                // objective count, dangerous terrain) are real draws and take the seed too.
                ERandomnessType.Probabilistic => new ProbabilisticDiceRoller(gameSettings.DiceSeed),
                // #167: an explicit seed makes Realistic-mode runs repeatable (scenario testing / repro cases).
                ERandomnessType.Realistic => new RealisticDiceRoller(gameSettings.DiceSeed),
                _ => throw new ArgumentOutOfRangeException()
            };

            return diceRoller;
        }

        private async Task LaunchStateMachineOnceReady(StateMachine<IGameContext> stateMachine, IGameContext context, bool resume)
        {
            // Yield back to the constructor immediately. WaitUntilAllSlotsReady + the resolvers can all
            // complete synchronously (e.g. headless with piped/AI input), which would otherwise run the
            // entire game inside the constructor — before callers (CliApp) can subscribe to OnGameEnded,
            // so the game-ended signal would be missed and the app would hang post-game. Yielding ensures
            // construction returns first and the game runs on a continuation.
            await Task.Yield();

            // Block until every assigned slot's controller reports ready (#036): a network player's
            // controller completes when its client sends PostLaunchPlayerReadyMessage (after the client
            // builds its resolvers in AssignInterfaces); a local player when its resolver registry is
            // assigned; an AI player immediately. Without this the host could enter the state machine and
            // request a decision before a client is wired up to answer it.
            Debug.WriteLine("Awaiting players to be ready.");
            await _playerSlotManager.WaitUntilAllSlotsReady();
            Debug.WriteLine("All players are ready. Launching stage machine.");

            try
            {
                if (resume)
                {
                    // Skip map setup + deployment; resume directly in the main phase from the saved flow state.
                    await stateMachine.Enter(context, nameof(MainPhaseRoundStage));
                }
                else
                {
                    await stateMachine.Enter(context);
                }
            }
            catch (RequestMessageSender.PlayerDisconnectedException disconnect)
            {
                // A player leaving mid-game is a normal event, not an engine fault: end the game with a
                // plain message and no alarming stack dump. The Disconnect outcome (#187) tells the host
                // this store is intact and worth writing a recovery save from — the state machine has
                // finished unwinding here, so this is the most quiescent the store ever is mid-game.
                string message = DescribePlayerLeft(_playerSlotManager, disconnect.PlayerID);
                Console.WriteLine($"Game ended: {message}");
                RaiseGameCompleted(GameResult.ForDisconnect(message));
            }
            catch (FDG.Simulation.SimulationStopSignal)
            {
                // #191 B1 5c: a simulation reaching the end of its prescribed line - the caller
                // already has the snapshot it wanted. Since #191 R9 the engine's own hook stops
                // cooperatively (DeterminePlayerTurnStage notifies completion and returns), so this
                // catch is the quiet fallback for a hook that still throws. Deliberately silent: a
                // state-machine stack trace per simulation is noise and cost; real faults below still
                // print in full.
                RaiseGameCompleted(GameResult.ForFault("Simulation stopped at the end of its line."));
            }
            catch (Exception ex)
            {
                // The state machine runs detached, so an unhandled fault would otherwise be unobserved
                // (silent hang) — surface it and end the game so the app can exit cleanly.
                Console.WriteLine($"[GAME ERROR] State machine faulted: {ex}");
                RaiseGameCompleted(GameResult.ForFault($"Game error: {ex.Message}"));
            }
        }

        // Single fan-out point for every way a game can finish (victory, disconnect, engine fault), so the
        // structured and legacy events can never disagree or fire without each other. Structured first:
        // OnGameEnded subscribers tear the game down (CliApp completes its TCS), so a later-raised
        // OnGameCompleted could arrive after the process has moved on.
        private void RaiseGameCompleted(GameResult result)
        {
            // #450: GameEnded is the recording's last event - recorded (and broadcast, ahead of the lobby's
            // GameEndedMessage) BEFORE the subscribers below run, since they write recovery saves and tear the
            // game down. Then the recording is closed: anything after this is teardown, not the game.
            if (_replayMarkers != null)
            {
                _replayMarkers.GameEnded(result);
                _replay?.Dispose();
            }

            OnGameCompleted?.Invoke(result);
            OnGameEnded?.Invoke(result.Message);
        }

        // Friendly end-of-game text for a player who dropped mid-game. Internal + static so it can be tested
        // directly without standing up a whole game. GetSlotByID throws if the slot is already gone, so it
        // falls back to a generic label rather than turning a lookup miss into a second fault.
        internal static string DescribePlayerLeft(PlayerSlotManager playerSlotManager, PlayerID playerID)
        {
            string name;
            try
            {
                name = playerSlotManager.GetSlotByID(playerID).Name;
            }
            catch
            {
                name = "A player";
            }

            return $"{name} left the game. The game has ended.";
        }

        private async void LaunchSingleTurnTester(GameContext gameContext)
        {
            SingleTurnStageTestBuilder testBuilder = new SingleTurnStageTestBuilder();

            //_stateMachine = new StateMachine<IGameContext>(new GDFStateMachineBuilder(), _gameContext);

            StateMachine<ISingleRoundContext> stateMachine = new StateMachine<ISingleRoundContext>(testBuilder, gameContext);

            int playerCount = _playerSlotManager.PlayerCount;

            float zOffset = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / (playerCount + 1);
            float xStart = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2;
            float xOffset = 0.1f; //Arbitrary.

            Dictionary<PlayerID, int> modelDeployCount = new Dictionary<PlayerID, int>();

            //Place the units on the board already since we're skipping deployment.
            foreach (DataBinding<UnitData> unit in gameContext.GameDataStore.GetAllDataBindings<UnitData>())
            {
                PlayerID playerID = unit.PlayerID();
                int playerSlotIndex = Array.IndexOf(_playerSlotManager.PlayerSlots, _playerSlotManager.GetSlotByID(playerID));
                float zPos = zOffset * (playerSlotIndex + 1);

                if(modelDeployCount.ContainsKey(playerID) == false)
                {
                    modelDeployCount[playerID] = 0;
                }

                foreach(DataBinding<ModelData> model in unit.ModelBindings())
                {
                    // Circumscribing radius so rectangular bases don't overlap in this fallback auto-deploy row. #150.
                    float xPos = xStart + modelDeployCount[playerID] * (xOffset + model.GetValue().BaseShape.CircumscribedRadiusInches * 2);

                    model.GetValue().SetPosition(new Position(xPos, zPos));

                    modelDeployCount[playerID]++;
                }
            }


            List<ITeam> teamOrder = gameContext.TableState.Teams.Objects.ToList();

            SingleRoundContext context = new SingleRoundContext(gameContext, teamOrder);

            // See LaunchStateMachineOnceReady: wait for every slot's controller to report ready (#036).
            Debug.WriteLine("Awaiting players to be ready.");
            await _playerSlotManager.WaitUntilAllSlotsReady();
            Debug.WriteLine("All players are ready. Launching stage machine.");

            _ = stateMachine.Enter(context);
        }

        /*
        private void LoadTestData()
        {
            float baseRadiusInches = 0.75f;
            List<Weapon> weapons = new List<Weapon>() { new Weapon("Weapon 1", 6, 2, 1) };
            List<SpecialRule> specialRules = new List<SpecialRule>() { new Rending() };

            int perTeamModelCount = 5;
            float spacing = 2.5f;

            float startX = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f - (perTeamModelCount / 2f * spacing);
            float team1StartY = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            float team2StartY = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;

            //Team 1.
            for (int i = 0; i < perTeamModelCount; i++)
            {
                Position position = new Position(startX + i * spacing, team1StartY);

                ModelData modelData = new ModelData(baseRadiusInches, weapons, specialRules, position, _gameDataStore);
                _gameDataStore.Create(modelData);
            }

            //Team 2.
            for (int i = 0; i < perTeamModelCount; i++)
            {
                Position position = new Position(startX + i * spacing, team2StartY);

                ModelData modelData = new ModelData(baseRadiusInches, weapons, specialRules, position, _gameDataStore);
                _gameDataStore.Create(modelData);
            }
        }
        */
    }
}
