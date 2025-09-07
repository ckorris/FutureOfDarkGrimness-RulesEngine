using FDG.Data;
using FDG.EngineInterface;
using FDG.MessageBus;
using FDG.StageResolution;
using FDG.TempVisuals;
using FDG.TextInterface;

namespace FDG.GameModel
{
    public class FDGGame_AsLocal : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry? StageResolverRegistry { get; private set; }

        public ILogMessageUI? LogMessageUI { get; private set; }

        public IPlayerMessageUI? PlayerMessageUI { get; private set; }

        public ITempVisualDrawer? TempVisualDrawer { get; private set; }


        private IMessageBusClient _messageBusClient;


        private List<PlayerID> _localPlayerIDs = new List<PlayerID>();

        public event Action OnStageResolverAssigned;

        private IReadableGameDataStore _gameDataStore;

        private List<NetworkedRequestMessageReceiver> _requestReceivers = new List<NetworkedRequestMessageReceiver>();


        public FDGGame_AsLocal(IReadableGameDataStore gameDataStore, IMessageBusClient messageBusClient)
        { 
            _gameDataStore = gameDataStore;
            TableState = new TableState(gameDataStore);

            _messageBusClient = messageBusClient;
        }

        public void AddLocalPlayerID(PlayerID playerID)
        {
            if(StageResolverRegistry != null)
            {
                throw new InvalidOperationException($"Called {nameof(AddLocalPlayerID)} after we assigned interfaces.");
            }

            _localPlayerIDs.Add(playerID);
        }

        public void AssignInterfaces(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI, 
            IStageResolverRegistry stageResolverRegistry, ITempVisualDrawer tempVisualDrawer)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            StageResolverRegistry = stageResolverRegistry;

            TempVisualDrawer = tempVisualDrawer;

            OutstandingTaskLister outstandingTaskLister = new OutstandingTaskLister(); //TODO: Don't pass in, will duplicate.

            //Note that as of writing, we've got no safeguards if multiple requests come in at the same time for different local players.
            //Not sure if we'll ever need that, but if so, it's currently up to the resolvers to handle.
            foreach( PlayerID playerID in _localPlayerIDs)
            {
                _requestReceivers.Add(new NetworkedRequestMessageReceiver(playerID, _messageBusClient, stageResolverRegistry,
                    outstandingTaskLister, _gameDataStore));
            }
                
            OnStageResolverAssigned?.Invoke();
        }
    }
}
