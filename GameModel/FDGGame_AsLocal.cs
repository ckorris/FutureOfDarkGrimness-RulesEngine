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


        private PlayerID _thisPlayerID;

        public event Action OnStageResolverAssigned;

        private IReadableGameDataStore _gameDataStore;

        private NetworkedRequestMessageReceiver _requestReceiver;


        public FDGGame_AsLocal(IReadableGameDataStore gameDataStore, IMessageBusClient messageBusClient, PlayerID thisPlayerID)
        { 
            _gameDataStore = gameDataStore;
            TableState = new TableState(gameDataStore);

            _messageBusClient = messageBusClient;
            _thisPlayerID = thisPlayerID;
        }

        public void AssignInterfaces(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI, 
            IStageResolverRegistry stageResolverRegistry, ITempVisualDrawer tempVisualDrawer)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            StageResolverRegistry = stageResolverRegistry;

            TempVisualDrawer = tempVisualDrawer;

            _requestReceiver = new NetworkedRequestMessageReceiver(_thisPlayerID, _messageBusClient, stageResolverRegistry,
                new OutstandingTaskLister(), _gameDataStore);

            OnStageResolverAssigned?.Invoke();


        }
    }
}
