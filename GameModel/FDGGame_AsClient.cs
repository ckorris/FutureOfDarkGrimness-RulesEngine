using FDG;
using FDG.Data;
using FDG.EngineInterface;
using FDG.MessageBus;
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


        private IMessageBusClient _messageBusClient;


        private PlayerID _thisPlayerID;


        private GameDataStore _gameDataStore;


        private GameDataUpdateReceiver _dataUpdateReceiver;


        private NetworkedRequestMessageReceiver _networkedRequestReceiver;

        public FDGGame_AsClient(IMessageBusClient messageBusClient, PlayerID thisPlayerID)
        {
            _messageBusClient = messageBusClient;
            _thisPlayerID = thisPlayerID;

            _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            TableState = new TableState(_gameDataStore);

            _dataUpdateReceiver = new GameDataUpdateReceiver(_gameDataStore, messageBusClient);
            _dataUpdateReceiver.RequestAllCurrentData();
        }

        public void AssignInterfaces(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI,
            IStageResolverRegistry stageResolverRegistry, ITempVisualDrawer tempVisualDrawer)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            StageResolverRegistry = stageResolverRegistry;

            _messageBusClient.RegisterForMessageEvent<LogChatNetworkMessage>(OnLogMessageReceived);
            _messageBusClient.RegisterForMessageEvent<PlayerChatNetworkMessage>(OnPlayerMessageReceived);

            _messageBusClient.RegisterForMessageEvent<AddTempVisualMessage>(OnAddTempVisualReceived);
            _messageBusClient.RegisterForMessageEvent<UpdateTempVisualTransformMessage>(OnUpdateTempVisualTransformReceived);
            _messageBusClient.RegisterForMessageEvent<UpdateTempVisualColorMessage>(OnUpdateTempVisualColorReceived);
            _messageBusClient.RegisterForMessageEvent<RemoveTempVisualMessage>(OnRemoveTempVisualReceived);
            _messageBusClient.RegisterForMessageEvent<ClearAllTempVisualsMessage>(OnClearTempVisualsReceived);

            _messageBusClient.SendCommandToHostAsync(new PostLaunchPlayerReadyMessage(_thisPlayerID));

            PlayerMessageUI.OnMessageSentByPlayer += SendChatMessage;

            TempVisualDrawer = tempVisualDrawer;

            _networkedRequestReceiver = new NetworkedRequestMessageReceiver(_thisPlayerID, _messageBusClient, stageResolverRegistry,
                new OutstandingTaskLister(), _gameDataStore);
        }

        private void OnLogMessageReceived(LogChatNetworkMessage message)
        {
            LogMessageUI?.DisplayLogMessage(message.LogMessage);
        }

        private void OnPlayerMessageReceived(PlayerChatNetworkMessage message)
        {
            PlayerMessageUI?.DisplayPlayerMessage(message.SendingPlayerName, message.MessageType, message.Message);
        }

        private void OnAddTempVisualReceived(AddTempVisualMessage message)
        {
            TempVisualDrawer?.AddVisual(message.TempVisual);
        }

        private void OnUpdateTempVisualTransformReceived(UpdateTempVisualTransformMessage message)
        {
            TempVisualDrawer?.UpdateVisualTransform(message.VisualID, message.Position, message.Rotation, message.Scale);
        }

        private void OnUpdateTempVisualColorReceived(UpdateTempVisualColorMessage message)
        {
            TempVisualDrawer?.UpdateVisualColor(message.TempVisualID, message.Color);
        }

        private void OnRemoveTempVisualReceived(RemoveTempVisualMessage message)
        {
            TempVisualDrawer?.RemoveVisual(message.VisualID);
        }

        private void OnClearTempVisualsReceived(ClearAllTempVisualsMessage message)
        {
            TempVisualDrawer?.ClearAllVisuals();
        }


        private void SendChatMessage(string message, EChatMessageType messageType)
        {
            NetworkPlayerSubmitChatMessage chatMessage = new NetworkPlayerSubmitChatMessage(messageType, message);
            _messageBusClient.SendCommandToHostAsync(chatMessage);
        }
    }
}
