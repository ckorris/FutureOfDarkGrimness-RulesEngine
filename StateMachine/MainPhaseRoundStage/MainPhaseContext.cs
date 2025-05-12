
using FDG.Data;

namespace FDG.Stages
{
    public interface IMainPhaseContext : IGameContextAccessor
    {
        public List<DataBinding<UnitData>> UnactivatedUnits { get; }
    }

    public class MainPhaseContext : IMainPhaseContext
    {
        public IGameContext GameContext { get; private set; }

        public List<DataBinding<UnitData>> UnactivatedUnits { get; private set; }

        public MainPhaseContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }
    }
}