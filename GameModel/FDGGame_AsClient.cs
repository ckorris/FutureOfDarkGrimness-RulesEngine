using FDG;
using FDG.Data;
using FDG.EngineInterface;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Messages;
using FDG.StageResolution;
using FDG.StageResolution.Previews;
using FDG.TextInterface;

namespace FDG.GameModel
{
    public class FDGGame_AsClient : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry StageResolverRegistry { get; private set; }

        public ILogMessageUI? LogMessageUI { get; private set; }

        public IPlayerMessageUI? PlayerMessageUI { get; private set; }

        public IPresentationSink? PresentationSink { get; private set; }

        public IPreviewChannel PreviewChannel { get; }

        public IPreviewFeed PreviewFeed { get; }

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

            // #277 preview sharing: publish via the host relay; receive everyone else's.
            PreviewChannel = new PreviewChannel(messageBusClient);
            PreviewFeed = new PreviewFeed(messageBusClient, new List<PlayerID> { thisPlayerID });
        }

        public void AssignInterfaces(ILogMessageUI? logMessageUI, IPlayerMessageUI? playerMessageUI,
            IStageResolverRegistry stageResolverRegistry,
            IPresentationSink? presentationSink,
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

            _messageBusClient.RegisterForMessageEvent<PresentBeatMessage>(OnPresentBeatReceived);

            _messageBusClient.SendCommandToHostAsync(new PostLaunchPlayerReadyMessage(_thisPlayerID));

            PresentationSink = presentationSink;

            _requestReceiver = new NetworkedRequestMessageReceiver(_thisPlayerID, _messageBusClient,
                stageResolverRegistry, _gameDataStore);

            if (outstandingTaskDisplay != null)
            {
                _outstandingTaskLister = new OutstandingTaskLister(_messageBusClient);
                outstandingTaskDisplay.AssignLister(_outstandingTaskLister);
            }
        }

        private void OnPresentBeatReceived(PresentBeatMessage message)
        {
            PresentationSink?.OnBeat(message.Beat);
        }
    }
}
