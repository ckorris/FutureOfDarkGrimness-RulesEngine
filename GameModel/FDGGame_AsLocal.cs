using FDG.Data;
using FDG.EngineInterface;
using FDG.StageResolution;
using FDG.TextInterface;
using Microsoft.Win32;

namespace FDG.GameModel
{
    public class FDGGame_AsLocal : IFDGGame
    {
        public ITableState TableState { get; }

        public IStageResolverRegistry? StageResolverRegistry { get; private set; }

        public ILogMessageUI? LogMessageUI { get; private set; }

        public IPlayerMessageUI? PlayerMessageUI { get; private set; }

        public event Action OnStageResolverAssigned;

        private IReadableGameDataStore _gameDataStore;


        public FDGGame_AsLocal(IReadableGameDataStore gameDataStore)
        { 
            _gameDataStore = gameDataStore;
            TableState = new TableState(gameDataStore);
        }

        public void AssignInterfaces(IStageResolverRegistry registry)
        {
            

            //TODO: Need to tell server we're ready.
        }

        public void AssignInterfaces(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI, 
            IStageResolverRegistry stageResolverRegistry)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            StageResolverRegistry = stageResolverRegistry;

            OnStageResolverAssigned?.Invoke();
        }
    }
}
