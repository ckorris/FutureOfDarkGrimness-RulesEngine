using FDG.Data;
using FDG.MessageBus;
using FDG.SaveLoad;
using FDG.Network.Connection;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.Stages;
using FDG.StateMachine.StateMachineBuilders;
using FDG.TempVisuals;
using FDG.TextInterface;
using FDG.Utilities;
using FutureOfDarkGrimness.StateMachine.StateMachineBuilders;
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
        private NetworkedTempVisualDrawer _tempVisualRelayer;

        private static bool TEST_SINGLE_TURN = false; //Turn on to skip most of the game and just do one run of a model's activation.

        public FDGServer(IReadWriteableGameDataStore gameDataStore, IMessageBusHost messageBusHost,
            GameSettings gameSettings, PlayerSlot[] playerSlots)
        {
            Debug.WriteLine($"Started {nameof(FDGServer)}.");

            _gameDataStore = gameDataStore;
            _messageBusHost = messageBusHost;
            _synchronizer = new GameDataUpdateSender(gameDataStore, messageBusHost);

            //For players/player slots, work backwards from here to create what you need to send updates to players,
            //then what you need to make that thing, then what you need for that, etc. until you lead back to these args.

            _playerSlotManager = new PlayerSlotManager(playerSlots);

            AddTeamDataToGameDataStore(playerSlots, gameDataStore);

            CreateArmies(playerSlots, gameDataStore);

            LogAndChatMessageRelayer chatMessageRelayer = new LogAndChatMessageRelayer(_playerSlotManager);

            ITextOutput textOutput = new PlayerLogSender(chatMessageRelayer);

            TableState tableState = new TableState(_gameDataStore);

            TempVisualRelayer tempVisualRelayer = new TempVisualRelayer(_playerSlotManager);

            RequestMessageSender requestMessageSender = new RequestMessageSender(messageBusHost, gameDataStore, 
                _playerSlotManager);

            _gameContext = new GameContext(textOutput, GetDiceRoller(gameSettings), requestMessageSender,
                tableState, _gameDataStore, tempVisualRelayer, gameSettings);
            _gameContext.OnGameEnded += result => OnGameEnded?.Invoke(result);

            // #042 creation-time rules (Tough): now that the evaluator exists, fire OnUnitCreated for
            // every army unit and apply its results (e.g. set each model's max wounds). Done here, not
            // in CreateArmies, because CreateArmies runs before the GameContext/RuleEvaluator is built.
            foreach (DataBinding<UnitData> unitBinding in _gameContext.GameDataStore.GetAllDataBindings<UnitData>())
            {
                UnitCreationRules.Apply(unitBinding.GetValue(), _gameContext.RuleEvaluator);
            }

            if (TEST_SINGLE_TURN)
            {
                LaunchSingleTurnTester(_gameContext);
                return;
            }

            _stateMachine = new StateMachine<IGameContext>(new GDFStateMachineBuilder(), _gameContext);

            //For test, make a thing. 
            //LoadTestData();

            _ = LaunchStateMachineOnceReady(_stateMachine, _gameContext);
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
                UnitData unitData = new UnitData(playerID, unitEntry, gameDataStore);
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
        //rule (one with no definition in the catalog) is skipped with a warning so partial armies
        //still load and the rules that ARE implemented still fire.
        private void AttachRulesFromArmyList(UnitData unitData, UnitFileEntry unitEntry, IRuleResolver ruleResolver)
        {
            foreach (SpecialRuleEntry ruleEntry in unitEntry.SpecialRules)
            {
                (string lookupName, IReadOnlyList<RuleArgument> arguments) = DescribeRuleEntry(ruleEntry);

                if (ruleResolver.TryResolve(lookupName, out ResolvedRule resolved))
                {
                    unitData.AttachRuleDefinition(
                        new ResolvedRule(ruleEntry.PrintableName, resolved.Definition, arguments));
                }
                else
                {
                    Debug.WriteLine(
                        $"[#042] Skipping unimplemented special rule '{ruleEntry.PrintableName}' on unit '{unitData.Name}'.");
                }
            }
        }

        //Maps an army-list rule entry to the canonical name used for registry lookup plus any
        //per-instance arguments. Core rules look up by name; numeric rules carry their value as a
        //single Int argument; aliases look up by the rule they rename.
        private static (string lookupName, IReadOnlyList<RuleArgument> arguments) DescribeRuleEntry(SpecialRuleEntry ruleEntry)
        {
            switch (ruleEntry)
            {
                case SpecialRuleEntry_CoreNumeric numeric:
                    return (numeric.Name, new RuleArgument[] { new RuleArgument.Int(numeric.NumericValue) });
                case SpecialRuleEntry_Alias alias:
                    return DescribeRuleEntry(alias.AliasedRule);
                case SpecialRuleEntry_Core core:
                    return (core.Name, Array.Empty<RuleArgument>());
                default:
                    return (ruleEntry.PrintableName, Array.Empty<RuleArgument>());
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

        private async Task LaunchStateMachineOnceReady(StateMachine<IGameContext> stateMachine, IGameContext context)
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
                await stateMachine.Enter(context);
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
            List<Weapon> weapons = new List<Weapon>() { new Weapon("Weapon 1", 6, 2, 1, new HashSet<ISpecialRule_Weapon>()) };
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
