using FDG;
using FDG.Data;
using FDG.EngineInterface;
using FDG.Network.Connection;
using FDG.Network.Synchronization;
using FDG.StageResolution;

namespace F.GameModel
{
    public class FDGGame_AsClient : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry StageResolverRegistry { get; }

        private ICommandDispatcher _commandDispatcher;

        private GameDataStore _gameDataStore;

        private GameDataUpdateReceiver _dataUpdateReceiver;

        public FDGGame_AsClient(ICommandDispatcher commandDispatcher)
        {
            _commandDispatcher = commandDispatcher;

            _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            TableState = new TableState(_gameDataStore);
            StageResolverRegistry = new StageResolverRegistry();

            _dataUpdateReceiver = new GameDataUpdateReceiver(_gameDataStore, commandDispatcher);
            _dataUpdateReceiver.RequestAllCurrentData();
        }
    }
}
