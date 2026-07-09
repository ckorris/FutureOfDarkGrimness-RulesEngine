using FDG.Data;
using FDG.MessageBus;
using FDG.SaveLoad;
using FDG.Network.Connection;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
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
        private PlayerSlotManager _playerSlotManager;
        private GameContext _gameContext;
        private StateMachine<IGameContext> _stateMachine;

        // The presentation clock the GUI host injects (null → instant, for headless/automated/resume).
        // Stored so the extracted BuildContextAndLaunch can reach it on both new-game and resume paths.
        private IPresentationClock? _presentationClock;

        // The shared army-load rule registry (core catalog + every army's embedded rules), built by
        // BuildRuleResolver on BOTH the new-game and resume paths. Threaded into GameContext →
        // RuleEvaluator so token-granted rules (auras / "gains rule X" buffs) resolve their names back to
        // definitions — including grants restored from a save (#101). Nullable only for the brief window
        // before either constructor builds it.
        private IRuleResolver? _ruleResolver;

        private static bool TEST_SINGLE_TURN = false; //Turn on to skip most of the game and just do one run of a model's activation.

        public FDGServer(IReadWriteableGameDataStore gameDataStore, IMessageBusHost messageBusHost,
            GameSettings gameSettings, PlayerSlot[] playerSlots, IPresentationClock? presentationClock = null)
        {
            Debug.WriteLine($"Started {nameof(FDGServer)} (new game).");

            _gameDataStore = gameDataStore;
            _messageBusHost = messageBusHost;
            _presentationClock = presentationClock;
            _synchronizer = new GameDataUpdateSender(gameDataStore, messageBusHost);

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
        public FDGServer(IReadWriteableGameDataStore loadedGameDataStore, IMessageBusHost messageBusHost,
            PlayerSlot[] playerSlots, IPresentationClock? presentationClock = null)
        {
            Debug.WriteLine($"Started {nameof(FDGServer)} (resume).");

            _gameDataStore = loadedGameDataStore;
            _messageBusHost = messageBusHost;
            _presentationClock = presentationClock;
            _synchronizer = new GameDataUpdateSender(loadedGameDataStore, messageBusHost);
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
            BuildRuleResolver(playerSlots);

            BuildContextAndLaunch(progress.Settings, applyCreationRules: false, resumeProgress: progress);
        }

        private void BuildContextAndLaunch(GameSettings gameSettings, bool applyCreationRules, GameProgressData? resumeProgress)
        {
            LogAndChatMessageRelayer chatMessageRelayer = new LogAndChatMessageRelayer(_playerSlotManager);

            ITextOutput textOutput = new PlayerLogSender(chatMessageRelayer);

            TableState tableState = new TableState(_gameDataStore);

            // The host owns presentation pacing. Default to instant (headless / automated /
            // tests stay deterministic); the GUI host injects a real-time clock so the battle
            // unfolds at a presentable tempo. State computation is never paced — only emission.
            IPresentationClock presentationClock_ = _presentationClock ?? new InstantPresentationClock();
            PresentationRelayer presentationRelayer = new PresentationRelayer(_playerSlotManager, presentationClock_);

            RequestMessageSender requestMessageSender = new RequestMessageSender(_messageBusHost, _gameDataStore,
                _playerSlotManager, textOutput);

            _gameContext = new GameContext(textOutput, GetDiceRoller(gameSettings), requestMessageSender,
                tableState, _gameDataStore, presentationRelayer, gameSettings, resumeProgress, _ruleResolver);
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
        /// a new game) doesn't run. On resume the army files may be vestigial (armies already live in the
        /// loaded store), so embedded custom rules only re-resolve when the file survived — core-rule grants
        /// always do.
        /// </summary>
        private RuleResolver BuildRuleResolver(PlayerSlot[] playerSlots)
        {
            RuleResolver ruleResolver = GameBootstrap.BuildRuleResolver(playerSlots);
            _ruleResolver = ruleResolver; // #101: shared in-game for granted-rule projection (auras / buffs)
            return ruleResolver;
        }

        private IDiceRoller GetDiceRoller(GameSettings gameSettings)
        {
            IDiceRoller diceRoller = gameSettings.RandomnessType switch
            {
                ERandomnessType.Probabilistic => new ProbabilisticDiceRoller(),
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
                // plain message and no alarming stack dump. Full mid-game recovery (auto-save + rejoin) is
                // work item #187.
                string message = DescribePlayerLeft(_playerSlotManager, disconnect.PlayerID);
                Console.WriteLine($"Game ended: {message}");
                RaiseGameCompleted(GameResult.ForFault(message));
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
                int playerSlotIndex = Array.IndexOf(_playerSlotManager._playerSlots, _playerSlotManager.GetSlotByID(playerID));
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
