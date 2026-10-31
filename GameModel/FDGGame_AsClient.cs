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

        public IReadOnlyList<PlayerID> LocalPlayerIDs => new[] { _thisPlayerID };

        /// <summary>#451: this client's own recording of the game, from construction on.</summary>
        public Replay.IReplaySource? Replay => _replay;

        internal Replay.ClientReplayRecorder ReplayRecorder => _replay;

        private readonly Replay.ClientReplayRecorder _replay;


        private IReadWriteableGameDataStore _gameDataStore;


        private GameDataUpdateReceiver _dataUpdateReceiver;


        private NetworkedRequestMessageReceiver _requestReceiver;


        private OutstandingTaskLister _outstandingTaskLister;


        /// <param name="hostReplayHeader">The host's replay header from the launch message (#451), for this
        /// client's recording; null when the host sent none.</param>
        public FDGGame_AsClient(IReadWriteableGameDataStore gameDataStore, IMessageBusClient messageBusClient, PlayerID thisPlayerID,
            Replay.ReplayHeader? hostReplayHeader = null)
        {
            _gameDataStore = gameDataStore;
            _messageBusClient = messageBusClient;
            _thisPlayerID = thisPlayerID;

            //_gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            TableState = new TableState(_gameDataStore);

            _dataUpdateReceiver = new GameDataUpdateReceiver(_gameDataStore, messageBusClient);

            // #451: record from before the catch-up is even asked for, so nothing the host sends is missed. Its
            // request handler registers ahead of AssignInterfaces' resolver, so an own request is recorded before
            // its answer can come back.
            _replay = new Replay.ClientReplayRecorder(messageBusClient, _gameDataStore, thisPlayerID, hostReplayHeader);
            _dataUpdateReceiver.OnFullStateApplied += _replay.OnFullStateApplied;
            _dataUpdateReceiver.RequestAllCurrentData();

            // #280 preview sharing: publish via the host relay; receive everyone else's.
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
