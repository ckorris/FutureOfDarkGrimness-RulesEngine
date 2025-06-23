using FDG;
using FDG.Data;
using FDG.EngineInterface;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.Network.Synchronization;
using FDG.StageResolution;
using FDG.TextInterface;

namespace F.GameModel
{
    public class FDGGame_AsClient : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry StageResolverRegistry { get; private set; }

        public ILogMessageUI? LogMessageUI { get; private set; }

        public IPlayerMessageUI? PlayerMessageUI { get; private set; }

        private ICommandDispatcher _commandDispatcher;

        private PlayerID _thisPlayerID;

        private GameDataStore _gameDataStore;

        private GameDataUpdateReceiver _dataUpdateReceiver;

        public FDGGame_AsClient(ICommandDispatcher commandDispatcher, PlayerID thisPlayerID)
        {
            _commandDispatcher = commandDispatcher;
            _thisPlayerID = thisPlayerID;

            _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            TableState = new TableState(_gameDataStore);
            StageResolverRegistry = new StageResolverRegistry();

            _dataUpdateReceiver = new GameDataUpdateReceiver(_gameDataStore, commandDispatcher);
            _dataUpdateReceiver.RequestAllCurrentData();
        }

        public void AssignInterfaces(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI, IStageResolverRegistry stageResolverRegistry)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            StageResolverRegistry = stageResolverRegistry;

            _commandDispatcher.RegisterForMessageEvent<LogChatNetworkMessage>(OnLogMessageReceived);
            _commandDispatcher.RegisterForMessageEvent<PlayerChatNetworkMessage>(OnPlayerMessageReceived);

            _commandDispatcher.SendCommandAsync(new PostLaunchPlayerReadyMessage(_thisPlayerID));

            
        }

        private void OnLogMessageReceived(LogChatNetworkMessage message, ConnectionID _)
        {
            LogMessageUI?.DisplayLogMessage(message.LogMessage);
        }

        private void OnPlayerMessageReceived(PlayerChatNetworkMessage message, ConnectionID iD)
        {
            PlayerMessageUI?.DisplayPlayerMessage(message.SendingPlayerName, message.MessageType, message.Message);
        }

        
    }
}
