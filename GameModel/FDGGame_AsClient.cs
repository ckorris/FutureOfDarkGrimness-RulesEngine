using FDG;
using FDG.Data;
using FDG.EngineInterface;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.StageResolution;
using FDG.TempVisuals;
using FDG.TempVisuals.Messages;
using FDG.TextInterface;

namespace F.GameModel
{
    public class FDGGame_AsClient : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry StageResolverRegistry { get; private set; }

        public ILogMessageUI? LogMessageUI { get; private set; }

        public IPlayerMessageUI? PlayerMessageUI { get; private set; }

        public ITempVisualDrawer? TempVisualDrawer { get; private set; }


        private ICommandDispatcher _commandDispatcher;


        private PlayerID _thisPlayerID;


        private GameDataStore _gameDataStore;


        private GameDataUpdateReceiver _dataUpdateReceiver;


        private NetworkedRequestMessageReceiver _networkedRequestReceiver;

        public FDGGame_AsClient(ICommandDispatcher commandDispatcher, PlayerID thisPlayerID)
        {
            _commandDispatcher = commandDispatcher;
            _thisPlayerID = thisPlayerID;

            _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            TableState = new TableState(_gameDataStore);

            _dataUpdateReceiver = new GameDataUpdateReceiver(_gameDataStore, commandDispatcher);
            _dataUpdateReceiver.RequestAllCurrentData();
        }

        public void AssignInterfaces(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI,
            IStageResolverRegistry stageResolverRegistry, ITempVisualDrawer tempVisualDrawer)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            StageResolverRegistry = stageResolverRegistry;

            _commandDispatcher.RegisterForMessageEvent<LogChatNetworkMessage>(OnLogMessageReceived);
            _commandDispatcher.RegisterForMessageEvent<PlayerChatNetworkMessage>(OnPlayerMessageReceived);

            _commandDispatcher.RegisterForMessageEvent<AddTempVisualMessage>(OnAddTempVisualReceived);
            _commandDispatcher.RegisterForMessageEvent<UpdateTempVisualTransformMessage>(OnUpdateTempVisualTransformReceived);
            _commandDispatcher.RegisterForMessageEvent<UpdateTempVisualColorMessage>(OnUpdateTempVisualColorReceived);
            _commandDispatcher.RegisterForMessageEvent<RemoveTempVisualMessage>(OnRemoveTempVisualReceived);
            _commandDispatcher.RegisterForMessageEvent<ClearAllTempVisualsMessage>(OnClearTempVisualsReceived);

            _commandDispatcher.SendCommandAsync(new PostLaunchPlayerReadyMessage(_thisPlayerID));

            PlayerMessageUI.OnMessageSentByPlayer += SendChatMessage;

            TempVisualDrawer = tempVisualDrawer;

            _networkedRequestReceiver = new NetworkedRequestMessageReceiver(_thisPlayerID, _commandDispatcher, stageResolverRegistry,
                new OutstandingTaskLister(), _gameDataStore);
        }

        private void OnLogMessageReceived(LogChatNetworkMessage message, ConnectionID _)
        {
            LogMessageUI?.DisplayLogMessage(message.LogMessage);
        }

        private void OnPlayerMessageReceived(PlayerChatNetworkMessage message, ConnectionID _)
        {
            PlayerMessageUI?.DisplayPlayerMessage(message.SendingPlayerName, message.MessageType, message.Message);
        }

        private void OnAddTempVisualReceived(AddTempVisualMessage message, ConnectionID _)
        {
            TempVisualDrawer?.AddVisual(message.TempVisual);
        }

        private void OnUpdateTempVisualTransformReceived(UpdateTempVisualTransformMessage message, ConnectionID iD)
        {
            TempVisualDrawer?.UpdateVisualTransform(message.VisualID, message.Position, message.Rotation, message.Scale);
        }

        private void OnUpdateTempVisualColorReceived(UpdateTempVisualColorMessage message, ConnectionID iD)
        {
            TempVisualDrawer?.UpdateVisualColor(message.TempVisualID, message.Color);
        }

        private void OnRemoveTempVisualReceived(RemoveTempVisualMessage message, ConnectionID iD)
        {
            TempVisualDrawer?.RemoveVisual(message.VisualID);
        }

        private void OnClearTempVisualsReceived(ClearAllTempVisualsMessage message, ConnectionID iD)
        {
            TempVisualDrawer?.ClearAllVisuals();
        }


        private void SendChatMessage(string message, EChatMessageType messageType)
        {
            NetworkPlayerSubmitChatMessage chatMessage = new NetworkPlayerSubmitChatMessage(messageType, message);
            _commandDispatcher.SendCommandAsync(chatMessage);
        }
    }
}
