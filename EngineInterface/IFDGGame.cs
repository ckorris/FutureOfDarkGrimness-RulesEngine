using FDG.StageResolution;
using FDG.TempVisuals;
using FDG.TextInterface;

namespace FDG.EngineInterface
{
    /// <summary>
    /// TODO: I put this in the EngineInterface folder, and it feels like more things could be there.
    /// </summary>
    public interface IFDGGame
    {
        ITableState TableState { get;}

        ILogMessageUI? LogMessageUI { get;}

        IPlayerMessageUI? PlayerMessageUI { get;}

        IStageResolverRegistry? StageResolverRegistry { get; }

        ITempVisualDrawer? TempVisualDrawer { get; }

        void AssignInterfaces(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI,
            IStageResolverRegistry stageResolverRegistry, ITempVisualDrawer tempVisualDrawer);
    }
}
