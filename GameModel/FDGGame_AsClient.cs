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

        //private IReadableGameDataStore _gameDataStore;

        private IMessageBusClient _messageBusClient;


        private PlayerID _thisPlayerID;


        private IReadWriteableGameDataStore _gameDataStore;


        private GameDataUpdateReceiver _dataUpdateReceiver;


        private NetworkedRequestMessageReceiver _requestReceiver;


        private OutstandingTaskLister _outstandingTaskLister;


        public FDGGame_AsClient(IReadWriteableGameDataStore gameDataStore, IMessageBusClient messageBusClient, PlayerID thisPlayerID)
        {
            _gameDataStore = gameDataStore;
            _messageBusClient = messageBusClient;
            _thisPlayerID = thisPlayerID;

            //_gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            TableState = new TableState(_gameDataStore);

            _dataUpdateReceiver = new GameDataUpdateReceiver(_gameDataStore, messageBusClient);
            _dataUpdateReceiver.RequestAllCurrentData();
        }

        public void AssignInterfaces(ILogMessageUI? logMessageUI, IPlayerMessageUI? playerMessageUI,
            IStageResolverRegistry stageResolverRegistry, ITempVisualDrawer? tempVisualDrawer,
            IOutstandingListDisplay? outstandingTaskDisplay)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            if(logMessageUI != null || playerMessageUI != null)
            {
                LogChatMessageEndpoint logChatMessageListener = new LogChatMessageEndpoint(logMessageUI, playerMessageUI, TableState,
                    new List<PlayerID> { _thisPlayerID }, _messageBusClient);
            }

            StageResolverRegistry = stageResolverRegistry;

            _messageBusClient.RegisterForMessageEvent<AddTempVisualMessage>(OnAddTempVisualReceived);
            _messageBusClient.RegisterForMessageEvent<UpdateTempVisualTransformMessage>(OnUpdateTempVisualTransformReceived);
            _messageBusClient.RegisterForMessageEvent<UpdateTempVisualColorMessage>(OnUpdateTempVisualColorReceived);
            _messageBusClient.RegisterForMessageEvent<RemoveTempVisualMessage>(OnRemoveTempVisualReceived);
            _messageBusClient.RegisterForMessageEvent<ClearAllTempVisualsMessage>(OnClearTempVisualsReceived);

            _messageBusClient.SendCommandToHostAsync(new PostLaunchPlayerReadyMessage(_thisPlayerID));

            TempVisualDrawer = tempVisualDrawer;

            _requestReceiver = new NetworkedRequestMessageReceiver(_thisPlayerID, _messageBusClient,
                stageResolverRegistry, _gameDataStore);

            if (outstandingTaskDisplay != null)
            {
                _outstandingTaskLister = new OutstandingTaskLister(_messageBusClient);
                outstandingTaskDisplay.AssignLister(_outstandingTaskLister);
            }
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
    }
}
