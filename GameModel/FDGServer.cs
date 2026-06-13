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
        public event Action<string>? OnGameEnded;

        private IReadWriteableGameDataStore _gameDataStore;
        private IMessageBusHost _messageBusHost;
        private GameDataUpdateSender _synchronizer;
        private PlayerSlotManager _playerSlotManager;
        private GameContext _gameContext;
        private StateMachine<IGameContext> _stateMachine;

        // The presentation clock the GUI host injects (null → instant, for headless/automated/resume).
        // Stored so the extracted BuildContextAndLaunch can reach it on both new-game and resume paths.
        private IPresentationClock? _presentationClock;

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

            AddTeamDataToGameDataStore(playerSlots, gameDataStore);

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
                tableState, _gameDataStore, presentationRelayer, gameSettings, resumeProgress);
            _gameContext.OnGameEnded += result => OnGameEnded?.Invoke(result);

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



        private void AddTeamDataToGameDataStore(PlayerSlot[] playerSlots, IReadWriteableGameDataStore gameDataStore)
        {
            Dictionary<int, List<PlayerID>> teams = new Dictionary<int, List<PlayerID>>();

            //This assumes team numbers are unique already. But if we ever use -1 for
            //those not on a team or something like that, there will be issues.

            for(int i = 0; i < playerSlots.Length; i++)
            {
                PlayerSlot slot = playerSlots[i];

                int teamSlot = slot.TeamNumber;
                if(teams.ContainsKey(teamSlot) == false)
                {
                    teams.Add(teamSlot, new List<PlayerID>());
                }

                teams[teamSlot].Add(slot.PlayerID);
            }
            
            foreach(KeyValuePair<int, List<PlayerID>> kvp in teams)
            {
                TeamData teamData = new TeamData(kvp.Key, kvp.Value);
                DataReference teamReference = gameDataStore.Create(teamData);
            }
        }

        private void CreateArmies(PlayerSlot[] playerSlots, IReadWriteableGameDataStore gameDataStore)
        {
            RuleResolver ruleResolver = CoreRuleCatalog.CreateResolver();

            // #059: register every army's embedded rule definitions before any unit resolves its rule
            // names. Core rules are registered above; these override by name, so a template can retune a
            // core rule from data. Done for all armies up front since the resolver is shared in-game.
            for (int i = 0; i < playerSlots.Length; i++)
            {
                ArmyListRuleResolution.RegisterEmbeddedDefinitions(ruleResolver, playerSlots[i].ArmyListFile);
            }

            for (int i = 0; i < playerSlots.Length; i++)
            {
                CreateArmyDataFromArmyFile(playerSlots[i].PlayerID, playerSlots[i].ArmyListFile, gameDataStore, ruleResolver);
            }
        }

        private void CreateArmyDataFromArmyFile(PlayerID playerID, ArmyListFile armyListFile, IReadWriteableGameDataStore gameDataStore, IRuleResolver ruleResolver)
        {
            List<DataBinding<UnitData>> unitBindings = new List<DataBinding<UnitData>>(armyListFile.Units.Count);

            foreach (UnitFileEntry unitEntry in armyListFile.Units)
            {
                UnitData unitData = new UnitData(playerID, unitEntry, gameDataStore, ruleResolver);
                AttachRulesFromArmyList(unitData, unitEntry, ruleResolver);
                DataReference unitDataReference = gameDataStore.Create(unitData);
                DataBinding<UnitData> unitBinding = gameDataStore.GetDataBinding<UnitData>(unitDataReference);
                unitBindings.Add(unitBinding);
            }

            ArmyData armyData = new ArmyData(playerID, unitBindings);

            DataReference armyDataReference = gameDataStore.Create(armyData);
        }

        //Resolves each special rule named on the army-list entry against the rule registry and
        //attaches the resolved #042 definition to the unit. A valid-but-not-yet-implemented core
        //rule (one with no definition in the catalog) — or a weapon-scoped rule misauthored at
        //unit level (#027) — is skipped with a warning so partial armies still load and the
        //rules that ARE implemented still fire. Weapon-level rules attach inside the UnitData
        //constructor via the same ArmyListRuleResolution helper.
        private void AttachRulesFromArmyList(UnitData unitData, UnitFileEntry unitEntry, IRuleResolver ruleResolver)
        {
            foreach (SpecialRuleEntry ruleEntry in unitEntry.SpecialRules)
            {
                ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(
                    ruleResolver, ruleEntry, ERuleScope.Unit, $"unit '{unitData.Name}'");

                if (resolved != null)
                {
                    unitData.AttachRuleDefinition(resolved);
                }
            }
        }


        private IDiceRoller GetDiceRoller(GameSettings gameSettings)
        {
            IDiceRoller diceRoller = gameSettings.RandomnessType switch
            {
                ERandomnessType.Probabilistic => new ProbabilisticDiceRoller(),
                ERandomnessType.Realistic => new RealisticDiceRoller(),
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

            //TODO: Wait for all clients to indicate that they are connected and ready.
            //Await something.
            Debug.WriteLine("Awaiting players to be ready.");
            await _playerSlotManager.WaitUntilAllSlotsReady(); //Half a second. At least lets us test before implementing this.
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
            catch (Exception ex)
            {
                // The state machine runs detached, so an unhandled fault would otherwise be unobserved
                // (silent hang) — surface it and end the game so the app can exit cleanly.
                Console.WriteLine($"[GAME ERROR] State machine faulted: {ex}");
                OnGameEnded?.Invoke($"Game error: {ex.Message}");
            }
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
                    float xPos = xStart + modelDeployCount[playerID] * (xOffset + model.GetValue().BaseRadiusInches * 2);

                    model.GetValue().SetPosition(new Position(xPos, zPos));

                    modelDeployCount[playerID]++;
                }
            }


            List<ITeam> teamOrder = gameContext.TableState.Teams.Objects.ToList();

            SingleRoundContext context = new SingleRoundContext(gameContext, teamOrder);

            Debug.WriteLine("Awaiting players to be ready.");
            await _playerSlotManager.WaitUntilAllSlotsReady(); //Half a second. At least lets us test before implementing this.
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
