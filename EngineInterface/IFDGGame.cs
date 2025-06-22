using FDG.StageResolution;

namespace FDG.EngineInterface
{
    /// <summary>
    /// TODO: I put this in the EngineInterface folder, and it feels like more things could be there.
    /// </summary>
    public interface IFDGGame
    {
        ITableState TableState { get;}

        IStageResolverRegistry StageResolverRegistry { get; }

        void AssignStageResolverRegistry(IStageResolverRegistry registry);
    }
}
