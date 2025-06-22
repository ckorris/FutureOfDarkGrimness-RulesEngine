using FDG.Data;
using FDG.EngineInterface;
using FDG.StageResolution;

namespace FDG.GameModel
{
    public class FDGGame_AsLocal : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry? StageResolverRegistry { get; private set; }

        public event Action OnStageResolverAssigned;

        private IReadableGameDataStore _gameDataStore;


        public FDGGame_AsLocal(IReadableGameDataStore gameDataStore)
        { 
            _gameDataStore = gameDataStore;
            TableState = new TableState(gameDataStore);
        }

        public void AssignStageResolverRegistry(IStageResolverRegistry registry)
        {
            StageResolverRegistry = registry;

            OnStageResolverAssigned?.Invoke();

            //TODO: Need to tell server we're ready.
        }
    }
}
