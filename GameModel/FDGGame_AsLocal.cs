using FDG.Data;
using FDG.EngineInterface;
using FDG.MessageBus;
using FDG.Network.Messages;
using FDG.Presentation;
using FDG.StageResolution;
using FDG.StageResolution.Previews;
using FDG.TextInterface;

namespace FDG.GameModel
{
    public class FDGGame_AsLocal : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry? StageResolverRegistry { get; private set; }

        public ILogMessageUI? LogMessageUI { get; private set; }

        public IPlayerMessageUI? PlayerMessageUI { get; private set; }

        public IPresentationSink? PresentationSink { get; private set; }

        public IPreviewChannel PreviewChannel { get; }

        public IPreviewFeed PreviewFeed { get; }

        private IMessageBusClient _messageBusClient;


        private List<PlayerID> _localPlayerIDs = new List<PlayerID>();

        public IReadOnlyList<PlayerID> LocalPlayerIDs => _localPlayerIDs;

        public event Action OnStageResolverAssigned;

        private IReadableGameDataStore _gameDataStore;


        private List<NetworkedRequestMessageReceiver> _requestReceivers = new List<NetworkedRequestMessageReceiver>();


        private OutstandingTaskLister _outstandingTaskLister;


        public FDGGame_AsLocal(IReadableGameDataStore gameDataStore, IMessageBusClient messageBusClient)
        {
            _gameDataStore = gameDataStore;
            TableState = new TableState(gameDataStore);

            _messageBusClient = messageBusClient;

            // #280 preview sharing. The feed holds the LIVE local-player list (populated later via
            // AddLocalPlayerID) so it filters every local player, however many join before launch.
            PreviewChannel = new PreviewChannel(messageBusClient);
            PreviewFeed = new PreviewFeed(messageBusClient, _localPlayerIDs);
        }

        public void AddLocalPlayerID(PlayerID playerID)
        {
            if(StageResolverRegistry != null)
            {
                throw new InvalidOperationException($"Called {nameof(AddLocalPlayerID)} after we assigned interfaces.");
            }

            _localPlayerIDs.Add(playerID);
        }

        public void AssignInterfaces(ILogMessageUI? logMessageUI, IPlayerMessageUI? playerMessageUI,
            IStageResolverRegistry stageResolverRegistry,
            IPresentationSink? presentationSink,
            IOutstandingListDisplay? outstandingTaskDisplay)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            if (logMessageUI != null || playerMessageUI != null)
            {
                LogChatMessageEndpoint logChatMessageListener = new LogChatMessageEndpoint(logMessageUI, playerMessageUI, TableState,
                    _localPlayerIDs, _messageBusClient);
            }

            StageResolverRegistry = stageResolverRegistry;

            PresentationSink = presentationSink;

            OutstandingTaskLister outstandingTaskLister = new OutstandingTaskLister(_messageBusClient); //TODO: Don't pass in, will duplicate.

            //Note that as of writing, we've got no safeguards if multiple requests come in at the same time for different local players.
            //Not sure if we'll ever need that, but if so, it's currently up to the resolvers to handle.
            foreach( PlayerID playerID in _localPlayerIDs)
            {
                _requestReceivers.Add(new NetworkedRequestMessageReceiver(playerID, _messageBusClient, 
                    stageResolverRegistry, _gameDataStore));
            }

            if (outstandingTaskDisplay != null)
            {
                _outstandingTaskLister = new OutstandingTaskLister(_messageBusClient);
                outstandingTaskDisplay.AssignLister(_outstandingTaskLister);
            }

            OnStageResolverAssigned?.Invoke();
        }
    }
}
