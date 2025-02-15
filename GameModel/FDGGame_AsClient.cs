using FDG;
using FDG.Data;
using FDG.EngineInterface;
using FDG.Network.Connection;
using FDG.Network.Synchronization;
using FDG.StateMachine.StageResolution;

namespace F.GameModel
{
    public class FDGGame_AsClient : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry StageResolverRegistry { get; }

        private ICommandDispatcher _commandDispatcher;

        private GameDataStore _gameDataStore;

        private GameDataSynchronizer _synchronizer;

        public FDGGame_AsClient(ICommandDispatcher commandDispatcher)
        {
            _commandDispatcher = commandDispatcher;

            _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            TableState = new TableState(_gameDataStore);
            StageResolverRegistry = new StageResolverRegistry();

            _synchronizer = new GameDataSynchronizer(_gameDataStore, commandDispatcher);
            _synchronizer.RequestAllCurrentData();
        }
    }
}
