

namespace FDG.Stages
{

    public interface IPlayerTurnContext : IGameContextAccessor
    {
    }

    public class PlayerTurnContext : IPlayerTurnContext
    {
        public IGameContext GameContext { get; private set; }

        public PlayerTurnContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }
    }
}