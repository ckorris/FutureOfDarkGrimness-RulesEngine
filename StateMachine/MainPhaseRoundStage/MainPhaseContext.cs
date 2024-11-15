
namespace FDG.Stages
{
    public interface IMainPhaseContext : IGameContextAccessor
    {

    }

    public class MainPhaseContext : IMainPhaseContext
    {
        public IGameContext GameContext { get; private set; }

        public MainPhaseContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }
    }
}