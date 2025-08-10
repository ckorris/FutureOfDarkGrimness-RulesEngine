using FDG.Data;
using FDG.EngineInterface;
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

        public event Action OnStageResolverAssigned;

        private IReadableGameDataStore _gameDataStore;


        public FDGGame_AsLocal(IReadableGameDataStore gameDataStore)
        { 
            _gameDataStore = gameDataStore;
            TableState = new TableState(gameDataStore);
        }

        public void AssignInterfaces(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI, 
            IStageResolverRegistry stageResolverRegistry, ITempVisualDrawer tempVisualDrawer)
        {
            LogMessageUI = logMessageUI;

            PlayerMessageUI = playerMessageUI;

            StageResolverRegistry = stageResolverRegistry;

            OnStageResolverAssigned?.Invoke();

            TempVisualDrawer = tempVisualDrawer;
        }
    }
}
